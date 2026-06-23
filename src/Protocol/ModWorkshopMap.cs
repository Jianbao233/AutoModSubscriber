using System.Collections.Generic;

namespace AutoModSubscriber.Protocol;

/// <summary>
/// 全局静态 Map：manifest id -> Steam Workshop PublishedFileId。
///
/// 由 <see cref="ClientInitialInfoSidecarPatch"/> 在客机收到 host
/// 的 InitialGameInfoMessage 时填充；由 UI 层在弹订阅对话框时读取。
/// </summary>
public static class ModWorkshopMap
{
    private static readonly object _gate = new();
    private static readonly Dictionary<string, ulong> _map = new();
    private static bool _hostHasMod;

    public static int Count
    {
        get { lock (_gate) return _map.Count; }
    }

    /// <summary>
    /// host 端是否安装了 AutoModSubscriber。
    /// 由 sidecar 中的哨兵 key 决定，不依赖具体 mod 映射数量。
    /// </summary>
    public static bool HostHasMod
    {
        get { lock (_gate) return _hostHasMod; }
    }

    public static void Replace(IDictionary<string, ulong> entries, bool hostHasMod)
    {
        lock (_gate)
        {
            _map.Clear();
            foreach (var kv in entries) _map[kv.Key] = kv.Value;
            _hostHasMod = hostHasMod;
        }
    }

    public static void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _hostHasMod = false;
        }
    }

    public static bool TryGet(string manifestId, out ulong fileId)
    {
        lock (_gate) return _map.TryGetValue(manifestId, out fileId);
    }

    public static Dictionary<string, ulong> Snapshot()
    {
        lock (_gate) return new Dictionary<string, ulong>(_map);
    }
}