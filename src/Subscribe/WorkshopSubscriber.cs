using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Steamworks;

namespace AutoModSubscriber.Subscribe;

/// <summary>
/// 单例。封装 Steam Workshop 订阅 + 下载 + 安装的全流程。
///
/// 设计要点：
/// - 长期持有 Callback&lt;ItemInstalled_t&gt; / Callback&lt;DownloadItemResult_t&gt;
///   （Steamworks.NET 文档要求字段引用必须 outlive 回调）
/// - 不为每个 SubscribeItem 创建独立 CallResult；订阅本身极少失败，进入
///   Downloading 状态后通过 GetItemState / GetItemDownloadInfo / 全局
///   Callback 来收敛
/// - Poll 由 UI 层的 Godot Timer 调（每 250ms），统一推进超时、兜底
///   DownloadItem、刷新进度
/// </summary>
public sealed class WorkshopSubscriber : IDisposable
{
    public const long DefaultTimeoutMs = 5 * 60 * 1000;
    public const long KickAfterMs = 5 * 1000;

    private static WorkshopSubscriber? _instance;
    public static WorkshopSubscriber Instance => _instance ??= new WorkshopSubscriber();

    private readonly object _gate = new();
    private readonly Dictionary<ulong, SubscribeJob> _jobs = new();

    private Callback<ItemInstalled_t>? _cbInstalled;
    private Callback<DownloadItemResult_t>? _cbDownload;

    private long _timeoutMs = DefaultTimeoutMs;

    public bool IsSteamAvailable
    {
        get
        {
            try { return SteamAPI.IsSteamRunning(); }
            catch { return false; }
        }
    }

    private WorkshopSubscriber()
    {
        try
        {
            _cbInstalled = Callback<ItemInstalled_t>.Create(OnItemInstalled);
            _cbDownload  = Callback<DownloadItemResult_t>.Create(OnDownloadResult);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{ModuleInit.LogTag} WorkshopSubscriber callback registration failed: {ex}");
        }
    }

    /// <summary>
    /// 提交一批订阅任务。重复提交同一 fileId 时复用已有 job。
    /// </summary>
    public IReadOnlyList<SubscribeJob> Submit(IEnumerable<(string manifestId, ulong fileId)> items)
    {
        var jobs = new List<SubscribeJob>();
        lock (_gate)
        {
            foreach (var (manifestId, fileId) in items)
            {
                if (fileId == 0) continue;
                if (_jobs.TryGetValue(fileId, out var existing))
                {
                    jobs.Add(existing);
                    continue;
                }

                var job = new SubscribeJob(manifestId, fileId)
                {
                    StartedAtMs = System.Environment.TickCount64,
                };
                _jobs[fileId] = job;
                jobs.Add(job);
                StartJob(job);
            }
        }
        return jobs;
    }

    public Task WaitAll(IEnumerable<SubscribeJob> jobs)
        => Task.WhenAll(jobs.Select(j => j.Completion.Task));

    public SubscribeJob? Get(ulong fileId)
    {
        lock (_gate) return _jobs.TryGetValue(fileId, out var j) ? j : null;
    }

    public IReadOnlyList<SubscribeJob> Snapshot()
    {
        lock (_gate) return _jobs.Values.ToList();
    }

    /// <summary>
    /// UI 侧 Timer 每 ~250ms 调一次。
    /// 推进超时 / 兜底 DownloadItem / 刷新下载进度。
    /// </summary>
    public void Poll()
    {
        long now = System.Environment.TickCount64;
        lock (_gate)
        {
            foreach (var job in _jobs.Values)
            {
                if (job.IsTerminal) continue;
                try { PollOne(job, now); }
                catch (Exception ex)
                {
                    GD.PrintErr($"{ModuleInit.LogTag} Poll({job.FileId}) failed: {ex}");
                }
            }
        }
    }

    private void StartJob(SubscribeJob job)
    {
        try
        {
            var pf = new PublishedFileId_t(job.FileId);

            uint state = SteamUGC.GetItemState(pf);
            if (IsAlreadyInstalled(state))
            {
                GD.Print($"{ModuleInit.LogTag} {job.ManifestId} ({job.FileId}) already installed, skipping");
                job.Finish(SubscribeJobState.Installed);
                return;
            }

            job.State = SubscribeJobState.Subscribing;
            SteamUGC.SubscribeItem(pf);
            job.State = SubscribeJobState.Downloading;
            GD.Print($"{ModuleInit.LogTag} SubscribeItem({job.ManifestId}, {job.FileId}) submitted");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{ModuleInit.LogTag} StartJob({job.FileId}) failed: {ex}");
            job.Finish(SubscribeJobState.Failed, ex.Message);
        }
    }

    private void PollOne(SubscribeJob job, long now)
    {
        if (now - job.StartedAtMs > _timeoutMs)
        {
            GD.Print($"{ModuleInit.LogTag} Job timeout: {job.ManifestId} ({job.FileId})");
            job.Finish(SubscribeJobState.TimedOut, "timeout");
            return;
        }

        var pf = new PublishedFileId_t(job.FileId);
        uint state = SteamUGC.GetItemState(pf);

        if (IsAlreadyInstalled(state))
        {
            job.Finish(SubscribeJobState.Installed);
            return;
        }

        bool downloading = (state & (uint)EItemState.k_EItemStateDownloading) != 0
                        || (state & (uint)EItemState.k_EItemStateDownloadPending) != 0;

        if (downloading)
        {
            if (SteamUGC.GetItemDownloadInfo(pf, out var done, out var total))
            {
                job.BytesDownloaded = done;
                job.BytesTotal = total;
            }
        }
        else if (!job.Kicked && now - job.StartedAtMs > KickAfterMs)
        {
            GD.Print($"{ModuleInit.LogTag} Kick DownloadItem({job.ManifestId}, {job.FileId})");
            SteamUGC.DownloadItem(pf, bHighPriority: true);
            job.Kicked = true;
        }
    }

    private static bool IsAlreadyInstalled(uint state)
    {
        bool installed = (state & (uint)EItemState.k_EItemStateInstalled) != 0;
        bool needsUpdate = (state & (uint)EItemState.k_EItemStateNeedsUpdate) != 0;
        return installed && !needsUpdate;
    }

    private void OnItemInstalled(ItemInstalled_t ev)
    {
        ulong fileId = ev.m_nPublishedFileId.m_PublishedFileId;
        lock (_gate)
        {
            if (!_jobs.TryGetValue(fileId, out var job)) return;
            if (job.IsTerminal) return;
            GD.Print($"{ModuleInit.LogTag} ItemInstalled: {job.ManifestId} ({fileId})");
            job.Finish(SubscribeJobState.Installed);
        }
    }

    private void OnDownloadResult(DownloadItemResult_t ev)
    {
        ulong fileId = ev.m_nPublishedFileId.m_PublishedFileId;
        lock (_gate)
        {
            if (!_jobs.TryGetValue(fileId, out var job)) return;
            if (job.IsTerminal) return;

            if (ev.m_eResult == EResult.k_EResultOK)
            {
                // 等待 ItemInstalled_t 收尾
                job.State = SubscribeJobState.WaitingInstall;
            }
            else
            {
                GD.PrintErr($"{ModuleInit.LogTag} Download failed: {job.ManifestId} ({fileId}) -> {ev.m_eResult}");
                job.Finish(SubscribeJobState.Failed, ev.m_eResult.ToString());
            }
        }
    }

    public void Dispose()
    {
        _cbInstalled?.Dispose();
        _cbDownload?.Dispose();
        _cbInstalled = null;
        _cbDownload = null;
    }
}