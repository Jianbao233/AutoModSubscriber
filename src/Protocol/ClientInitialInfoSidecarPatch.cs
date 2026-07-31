using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;

namespace AutoModSubscriber.Protocol;

/// <summary>
/// 客机侧：在 InitialGameInfoMessage.Deserialize 之后扫描 versionInfo.otherMods，
/// 找到 sidecar 条目则：
///   1. 把解码出的 (id -&gt; fileId) 写入 ModWorkshopMap
///   2. 从 `versionInfo.otherMods` 中把 sidecar 条目移除，避免污染原版 non-gameplay
///      mod 比对逻辑（虽然 vanilla 也只 warn 不断连接，但移除更干净）。
///
/// 注意：Deserialize 是 struct 的实例方法。Harmony 对 struct 实例方法
/// 的 Postfix 需要 [HarmonyPatch] 在类型上 + ref __instance 参数。
/// </summary>
[HarmonyPatch(typeof(InitialGameInfoMessage), nameof(InitialGameInfoMessage.Deserialize))]
internal static class ClientInitialInfoSidecarPatch
{
    [HarmonyPostfix]
    public static void Postfix(ref InitialGameInfoMessage __instance)
    {
        try
        {
            var outcome = ProcessOtherMods(__instance.versionInfo.otherMods);
            switch (outcome)
            {
                case SidecarReceiveOutcome.Parsed:
                    GD.Print($"{ModuleInit.LogTag} Client parsed sidecar: hostHas={ModWorkshopMap.HostHasMod}, {ModWorkshopMap.Count} mapping(s)");
                    break;
                case SidecarReceiveOutcome.Invalid:
                    GD.Print($"{ModuleInit.LogTag} Client received invalid sidecar; workshop mapping cleared");
                    break;
                default:
                    GD.Print($"{ModuleInit.LogTag} Client received no sidecar; workshop mapping cleared");
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{ModuleInit.LogTag} ClientInitialInfoSidecarPatch failed: {ex}");
        }
    }

    internal static SidecarReceiveOutcome ProcessOtherMods(List<string>? list)
    {
        // InitialGameInfoMessage 是每次加入房间的状态边界；绝不能复用前一房间的映射。
        ModWorkshopMap.Clear();

        if (list == null || list.Count == 0)
            return SidecarReceiveOutcome.Missing;

        Dictionary<string, (ulong FileId, List<string> Deps)>? parsed = null;
        bool hostHas = false;
        bool sawSidecar = false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            string entry = list[i];
            if (!SidecarCodec.IsSidecarEntry(entry)) continue;

            sawSidecar = true;
            if (SidecarCodec.TryDecodeWithDeps(entry, out var map, out var sentinel))
            {
                parsed = map;
                hostHas = sentinel;
            }
            list.RemoveAt(i);
        }

        if (parsed == null)
            return sawSidecar ? SidecarReceiveOutcome.Invalid : SidecarReceiveOutcome.Missing;

        var entries = new Dictionary<string, ModWorkshopMap.ModEntry>(parsed.Count);
        foreach (var kv in parsed)
            entries[kv.Key] = new ModWorkshopMap.ModEntry { FileId = kv.Value.FileId, Dependencies = kv.Value.Deps };
        ModWorkshopMap.Replace(entries, hostHas);
        return SidecarReceiveOutcome.Parsed;
    }
}

internal enum SidecarReceiveOutcome
{
    Missing,
    Invalid,
    Parsed
}
