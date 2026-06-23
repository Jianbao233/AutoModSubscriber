using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using Steamworks;

namespace AutoModSubscriber.Protocol;

/// <summary>
/// Host 侧：在 InitialGameInfoMessage.Basic() 返回之后，把
/// (manifest id -&gt; workshopFileId) 映射编码成一条 sidecar，
/// 追加到 otherMods 末尾。
///
/// 映射来源（两路合并）：
///   1. ModManager.Mods 中 SteamWorkshop 来源的 mod，直接从 mod.path 切 fileId。
///   2. 扫描 host Steam 当前已订阅的所有 Workshop 条目，读 mod_manifest.json
///      取 id 建立 id -&gt; fileId 反向索引；对 ModManager.Mods 中任何 Loaded
///      但 source != SteamWorkshop（典型情况是 mods/ 目录覆盖了 workshop 副本，
///      或者 host 本地放了 mods/ 但同时也在 Steam 订阅了同名 mod）的项，
///      用此索引兜底补 fileId。
///
/// gameplayAffectingMods 完全不动，保证 vanilla 客机的 gameplay mod
/// 比对逻辑不受影响。
/// </summary>
[HarmonyPatch(typeof(InitialGameInfoMessage), nameof(InitialGameInfoMessage.Basic))]
internal static class HostInitialInfoSidecarPatch
{
    private const uint AppId = 2868840;

    [HarmonyPostfix]
    public static void Postfix(ref InitialGameInfoMessage __result)
    {
        try
        {
            var map = BuildIdToFileIdMap();

            // 永远写哨兵，让客机知道 host 装了本 mod
            map[SidecarCodec.HostSentinelKey] = 1;

            __result.otherMods ??= new List<string>();
            string encoded = SidecarCodec.Encode(map);
            __result.otherMods.Add(encoded);

            int withFileId = map.Count(kv => kv.Key != SidecarCodec.HostSentinelKey && kv.Value != 0);
            int withoutFileId = map.Count(kv => kv.Key != SidecarCodec.HostSentinelKey && kv.Value == 0);
            GD.Print($"{ModuleInit.LogTag} Host sidecar attached: {withFileId} with fileId, {withoutFileId} without");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{ModuleInit.LogTag} HostInitialInfoSidecarPatch failed: {ex}");
        }
    }

    private static Dictionary<string, ulong> BuildIdToFileIdMap()
    {
        var result = new Dictionary<string, ulong>();

        var subscribedIndex = BuildSubscribedManifestIndex();

        // 遍历所有 Loaded mod，把 gameplay-relevant 的全部登记进 sidecar，
        // 即使 fileId=0（无法订阅）也写入，方便客机区分"host 装了但非 workshop"
        // 与"host 没装本 mod"。
        foreach (var mod in ModManager.Mods)
        {
            string? id = mod.manifest?.id;
            if (string.IsNullOrEmpty(id)) continue;
            if (mod.state != ModLoadState.Loaded) continue;

            // 自己 (AutoModSubscriber) 无需登记
            if (id == ModuleInit.ModId) continue;

            // 只关心 gameplay-relevant 的，否则即使客机缺也不会断连
            bool affectsGameplay = mod.manifest?.affectsGameplay ?? true;
            if (!affectsGameplay) continue;

            ulong fileId = 0;
            if (mod.modSource == ModSource.SteamWorkshop)
            {
                fileId = ExtractFileIdFromPath(mod.path);
            }
            if (fileId == 0 && subscribedIndex.TryGetValue(id!, out var idx))
            {
                fileId = idx;
            }

            result[id!] = fileId;
        }

        return result;
    }

    /// <summary>
    /// 扫一遍 Steam 当前订阅的所有 Workshop 条目，读出每条的 manifest.id，
    /// 构建 id -&gt; fileId 索引。失败的条目静默跳过。
    /// </summary>
    private static Dictionary<string, ulong> BuildSubscribedManifestIndex()
    {
        var index = new Dictionary<string, ulong>();

        try
        {
            if (!SteamAPI.IsSteamRunning()) return index;

            uint count = SteamUGC.GetNumSubscribedItems();
            if (count == 0) return index;

            var fileIds = new PublishedFileId_t[count];
            uint actual = SteamUGC.GetSubscribedItems(fileIds, count);

            for (int i = 0; i < actual; i++)
            {
                var pf = fileIds[i];
                if (!SteamUGC.GetItemInstallInfo(pf, out _, out var folder, 512u, out _))
                    continue;
                if (string.IsNullOrEmpty(folder)) continue;

                string? manifestId = TryReadManifestId(folder);
                if (string.IsNullOrEmpty(manifestId)) continue;

                index[manifestId!] = pf.m_PublishedFileId;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{ModuleInit.LogTag} BuildSubscribedManifestIndex failed: {ex}");
        }

        return index;
    }

    /// <summary>
    /// 在 workshop 条目目录里找一个含顶层 "id" 字段的 *.json 当 manifest，
    /// 返回其 id。和游戏自身扫描逻辑保持宽松兼容
    /// （游戏会把目录里**任何** .json 当 manifest 候选）。
    /// </summary>
    private static string? TryReadManifestId(string folder)
    {
        try
        {
            // 优先 mod_manifest.json
            string preferred = Path.Combine(folder, "mod_manifest.json");
            if (File.Exists(preferred))
            {
                var id = TryParseManifestIdFromFile(preferred);
                if (!string.IsNullOrEmpty(id)) return id;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
            {
                var id = TryParseManifestIdFromFile(file);
                if (!string.IsNullOrEmpty(id)) return id;
            }

            foreach (var sub in Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly))
            {
                foreach (var file in Directory.EnumerateFiles(sub, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var id = TryParseManifestIdFromFile(file);
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private static string? TryParseManifestIdFromFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                return idProp.GetString();
            }
        }
        catch
        {
            // not a manifest, skip
        }
        return null;
    }

    /// <summary>
    /// Steam workshop 路径形如：
    ///   .../steamapps/workshop/content/&lt;AppId&gt;/&lt;workshopFileId&gt;/...
    /// 取 AppId 后第一段纯数字目录名作为 fileId。
    /// </summary>
    internal static ulong ExtractFileIdFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return 0;

        string normalized = path!.Replace('\\', '/');
        string marker = $"/workshop/content/{AppId}/";
        int idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 0;

        int start = idx + marker.Length;
        int end = normalized.IndexOf('/', start);
        string segment = end < 0 ? normalized.Substring(start) : normalized.Substring(start, end - start);

        return ulong.TryParse(segment, out var fileId) ? fileId : 0;
    }
}