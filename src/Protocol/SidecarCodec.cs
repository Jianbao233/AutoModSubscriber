using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AutoModSubscriber.Protocol;

/// <summary>
/// Sidecar 条目编/解码。
///
/// 协议：在 InitialGameInfoMessage.otherMods 末尾追加一条字符串
///   "__ams_map__-1.0\u0001&lt;base64-utf8 of compact json&gt;"
/// json 形如 {"RitsuLib":"3700001234","FreeLoadout":"3700004567"}
/// (workshopId 用字符串避免 long 转换问题)
///
/// vanilla 客机会把这条当作 non-gameplay mod；JoinFlow 在 non-gameplay
/// 差集上只 warn 不断连接，所以对 vanilla 安全。
///
/// 装本 mod 的客机由 ClientInitialInfoSidecarPatch 抽出此条目并解析。
/// </summary>
public static class SidecarCodec
{
    public const string SidecarTag = "__ams_map__-1.0";
    public const char Separator = '\u0001';

    /// <summary>
    /// 哨兵 key：sidecar 中固定写入此键 (value 任意非 0)，
    /// 客机据此判定"host 装了本 mod"。即使 host 端没解析出任何
    /// workshop fileId，只要这条存在就说明 host 装了本 mod。
    /// </summary>
    public const string HostSentinelKey = "__ams_host__";

    /// <summary>
    /// 用于 otherMods 整体识别：以 "<tag><sep>" 开头即为 sidecar。
    /// </summary>
    public static readonly string SidecarPrefix = SidecarTag + Separator;

    public static bool IsSidecarEntry(string entry) =>
        !string.IsNullOrEmpty(entry) && entry.StartsWith(SidecarPrefix, StringComparison.Ordinal);

    public static string Encode(IReadOnlyDictionary<string, ulong> map)
    {
        var strMap = new Dictionary<string, string>(map.Count);
        foreach (var kv in map) strMap[kv.Key] = kv.Value.ToString();

        string json = JsonSerializer.Serialize(strMap);
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return SidecarPrefix + b64;
    }

    public static bool TryDecode(string entry, out Dictionary<string, ulong> map)
    {
        return TryDecodeFull(entry, out map, out _);
    }

    public static bool TryDecodeFull(string entry, out Dictionary<string, ulong> map, out bool hostSentinel)
    {
        map = new Dictionary<string, ulong>();
        hostSentinel = false;
        if (!IsSidecarEntry(entry)) return false;

        try
        {
            string b64 = entry.Substring(SidecarPrefix.Length);
            byte[] bytes = Convert.FromBase64String(b64);
            string json = Encoding.UTF8.GetString(bytes);
            var strMap = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (strMap == null) return false;

            foreach (var kv in strMap)
            {
                if (kv.Key == HostSentinelKey) { hostSentinel = true; continue; }
                if (ulong.TryParse(kv.Value, out var fileId))
                    map[kv.Key] = fileId; // 允许 fileId=0
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}