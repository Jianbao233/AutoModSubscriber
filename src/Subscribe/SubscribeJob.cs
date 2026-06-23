using System.Threading.Tasks;

namespace AutoModSubscriber.Subscribe;

public enum SubscribeJobState
{
    Pending,
    Subscribing,
    Downloading,
    WaitingInstall,
    Installed,
    Failed,
    TimedOut,
}

public sealed class SubscribeJob
{
    public ulong FileId { get; }
    public string ManifestId { get; }
    public SubscribeJobState State { get; internal set; } = SubscribeJobState.Pending;
    public string? Error { get; internal set; }
    public ulong BytesDownloaded { get; internal set; }
    public ulong BytesTotal { get; internal set; }

    internal TaskCompletionSource<bool> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 兜底 DownloadItem 是否已经踢过一脚，避免重复触发。
    /// </summary>
    internal bool Kicked { get; set; }

    /// <summary>
    /// Job 创建时刻（Environment.TickCount64 毫秒），用于超时与兜底调度。
    /// </summary>
    internal long StartedAtMs { get; set; }

    public SubscribeJob(string manifestId, ulong fileId)
    {
        ManifestId = manifestId;
        FileId = fileId;
    }

    internal void Finish(SubscribeJobState terminal, string? error = null)
    {
        State = terminal;
        Error = error;
        Completion.TrySetResult(terminal == SubscribeJobState.Installed);
    }

    public bool IsTerminal =>
        State == SubscribeJobState.Installed ||
        State == SubscribeJobState.Failed ||
        State == SubscribeJobState.TimedOut;
}