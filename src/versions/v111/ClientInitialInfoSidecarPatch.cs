using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Connection;

namespace AutoModSubscriber.Protocol;

/// <summary>
/// 客机侧：在 HandshakeManager.TryReadHandshakeMessage 返回之后，
/// 从握手结果里的远端 PeerVersionInfo.otherMods 扫描 sidecar 条目：
///   1. 把解码出的 (id -&gt; fileId) 写入 ModWorkshopMap
///   2. 从 `otherMods` 中把 sidecar 条目移除，避免污染原版 non-gameplay
///      mod 比对逻辑（虽然 vanilla 也只 warn 不断连，但移除更干净）。
///
/// v0.1.4 (v0.111.0 适配)：v0.111.0 握手重构后，mod 列表不再随
/// InitialGameInfoMessage 传输，而是作为 PeerVersionInfo 在握手阶段
/// （HandshakeManager.TryReadHandshakeMessage）解析。本 patch 的目标
/// 相应迁移到该方法；握手结果 HandshakeResult 携带远端 PeerVersionInfo。
///
/// 注意：TryReadHandshakeMessage 是 private 实例方法，返回 struct
/// HandshakeResult —— Harmony Postfix 需 ref __result。本 patch 在
/// host / client 两侧都会命中（各自解析对方握手消息）：client 从 host
/// 的 sidecar 填 ModWorkshopMap 供弹窗使用；host 从 client 的 sidecar
/// 提取后仅移除（无副作用）。
/// </summary>
[HarmonyPatch]
internal static class ClientInitialInfoSidecarPatch
{
    private static bool Prepare()
    {
        return AccessTools.Method(typeof(HandshakeManager), "TryReadHandshakeMessage") != null;
    }

    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(HandshakeManager), "TryReadHandshakeMessage");
    }

    [HarmonyPostfix]
    public static void Postfix(ref HandshakeResult __result)
    {
        try
        {
            var remoteVersionInfo = __result.remoteVersionInfo;
            var outcome = remoteVersionInfo.HasValue
                ? ProcessOtherMods(remoteVersionInfo.Value.otherMods)
                : SidecarReceiveOutcome.Missing;
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
        // 握手是每次加入房间的状态边界；绝不能复用前一房间的映射。
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
