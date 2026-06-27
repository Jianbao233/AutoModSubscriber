using System.Collections.Generic;
using System.Linq;

namespace AutoModSubscriber.Protocol;

/// <summary>
/// 全局静态 Map：manifest id -> (Steam Workshop PublishedFileId, dependencies)。
///
/// 由 <see cref="ClientInitialInfoSidecarPatch"/> 在客机收到 host
/// 的 InitialGameInfoMessage 时填充；由 UI 层在弹订阅对话框时读取。
/// </summary>
public static class ModWorkshopMap
{
    public sealed class ModEntry
    {
        public ulong FileId;
        public List<string> Dependencies = new();
    }

    private static readonly object _gate = new();
    private static readonly Dictionary<string, ModEntry> _map = new();
    private static bool _hostHasMod;

    public static int Count
    {
        get { lock (_gate) return _map.Count; }
    }

    /// <summary>
    /// host 端是否安装了 AutoModSubscriber。
    /// </summary>
    public static bool HostHasMod
    {
        get { lock (_gate) return _hostHasMod; }
    }

    public static void Replace(IDictionary<string, ModEntry> entries, bool hostHasMod)
    {
        lock (_gate)
        {
            _map.Clear();
            foreach (var kv in entries) _map[kv.Key] = kv.Value;
            _hostHasMod = hostHasMod;
        }
    }

    /// <summary>
    /// 旧接口兼容：只写 fileId，无 deps。
    /// </summary>
    public static void Replace(IDictionary<string, ulong> entries, bool hostHasMod)
    {
        var dict = new Dictionary<string, ModEntry>(entries.Count);
        foreach (var kv in entries)
            dict[kv.Key] = new ModEntry { FileId = kv.Value };
        Replace(dict, hostHasMod);
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
        lock (_gate)
        {
            if (_map.TryGetValue(manifestId, out var entry))
            {
                fileId = entry.FileId;
                return true;
            }
            fileId = 0;
            return false;
        }
    }

    public static bool TryGetEntry(string manifestId, out ModEntry? entry)
    {
        lock (_gate)
        {
            return _map.TryGetValue(manifestId, out entry);
        }
    }

    public static Dictionary<string, ModEntry> Snapshot()
    {
        lock (_gate) return new Dictionary<string, ModEntry>(_map);
    }
}