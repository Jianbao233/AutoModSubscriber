using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AutoModSubscriber.Protocol;

/// <summary>
/// Sidecar 条目编/解码。
///
/// 协议 v2：json 格式为
///   {"__ams_host__":"1","ModId":{"f":"fileId","d":["Dep1","Dep2"]},...}
/// f = workshop fileId (字符串)
/// d = dependencies (manifest.dependencies 中的 id 列表)
///
/// 向后兼容 v1：如果 value 是纯字符串，则视为 fileId，deps 为空。
/// </summary>
public static class SidecarCodec
{
    public const string SidecarTag = "__ams_map__-1.0";
    public const char Separator = '\u0001';
    public const string HostSentinelKey = "__ams_host__";
    public static readonly string SidecarPrefix = SidecarTag + Separator;

    public static bool IsSidecarEntry(string entry) =>
        !string.IsNullOrEmpty(entry) && entry.StartsWith(SidecarPrefix, StringComparison.Ordinal);

    /// <summary>
    /// 编码：接受 id -> (fileId, deps) 映射。
    /// </summary>
    public static string Encode(IReadOnlyDictionary<string, (ulong FileId, List<string>? Deps)> map)
    {
        var obj = new Dictionary<string, object>(map.Count + 1);
        foreach (var kv in map)
        {
            var entry = new Dictionary<string, object>
            {
                ["f"] = kv.Value.FileId.ToString(),
            };
            if (kv.Value.Deps != null && kv.Value.Deps.Count > 0)
                entry["d"] = kv.Value.Deps;
            obj[kv.Key] = entry;
        }

        string json = JsonSerializer.Serialize(obj);
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return SidecarPrefix + b64;
    }

    /// <summary>
    /// 旧接口兼容：只编码 fileId，无 deps。
    /// </summary>
    public static string Encode(IReadOnlyDictionary<string, ulong> map)
    {
        var dict = new Dictionary<string, (ulong, List<string>?)>(map.Count);
        foreach (var kv in map)
            dict[kv.Key] = (kv.Value, null);
        return Encode(dict);
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
            using var doc = JsonDocument.Parse(json);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name == HostSentinelKey) { hostSentinel = true; continue; }

                // v2: value 是对象 {"f":"...","d":[...]}
                // v1: value 是纯字符串 (fileId)
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    ulong fid = 0;
                    if (prop.Value.TryGetProperty("f", out var fProp) && fProp.ValueKind == JsonValueKind.String)
                        ulong.TryParse(fProp.GetString(), out fid);
                    map[prop.Name] = fid;
                }
                else if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    if (ulong.TryParse(prop.Value.GetString(), out var fid))
                        map[prop.Name] = fid;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 解码并提取 dependencies。
    /// </summary>
    public static bool TryDecodeWithDeps(string entry, out Dictionary<string, (ulong FileId, List<string> Deps)> map, out bool hostSentinel)
    {
        map = new Dictionary<string, (ulong, List<string>)>();
        hostSentinel = false;
        if (!IsSidecarEntry(entry)) return false;

        try
        {
            string b64 = entry.Substring(SidecarPrefix.Length);
            byte[] bytes = Convert.FromBase64String(b64);
            string json = Encoding.UTF8.GetString(bytes);
            using var doc = JsonDocument.Parse(json);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name == HostSentinelKey) { hostSentinel = true; continue; }

                ulong fid = 0;
                var deps = new List<string>();

                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    if (prop.Value.TryGetProperty("f", out var fProp) && fProp.ValueKind == JsonValueKind.String)
                        ulong.TryParse(fProp.GetString(), out fid);
                    if (prop.Value.TryGetProperty("d", out var dProp) && dProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in dProp.EnumerateArray())
                        {
                            if (d.ValueKind == JsonValueKind.String)
                            {
                                var s = d.GetString();
                                if (!string.IsNullOrEmpty(s)) deps.Add(s!);
                            }
                        }
                    }
                }
                else if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    ulong.TryParse(prop.Value.GetString(), out fid);
                }

                map[prop.Name] = (fid, deps);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}