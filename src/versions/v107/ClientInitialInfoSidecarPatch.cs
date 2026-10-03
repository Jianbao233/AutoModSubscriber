using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;

namespace AutoModSubscriber.Protocol;

/// <summary>
/// 客机侧：在 InitialGameInfoMessage.Deserialize 之后扫描 otherMods，
/// 找到 sidecar 条目则：
///   1. 把解码出的 (id -&gt; fileId) 写入 ModWorkshopMap
///   2. 从 otherMods 中把 sidecar 条目移除，避免污染原版 non-gameplay
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
            var list = __instance.otherMods;

            if (list == null || list.Count == 0)
            {
                // 收到空消息时不主动 Clear，保留前一次解析结果
                return;
            }

            Dictionary<string, (ulong FileId, List<string> Deps)>? parsed = null;
            bool hostHas = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                string entry = list[i];
                if (!SidecarCodec.IsSidecarEntry(entry)) continue;

                if (SidecarCodec.TryDecodeWithDeps(entry, out var map, out var sentinel))
                {
                    parsed = map;
                    hostHas = sentinel;
                }
                list.RemoveAt(i);
            }

            if (parsed != null)
            {
                // 转换为 ModEntry 格式
                var entries = new Dictionary<string, ModWorkshopMap.ModEntry>(parsed.Count);
                foreach (var kv in parsed)
                    entries[kv.Key] = new ModWorkshopMap.ModEntry { FileId = kv.Value.FileId, Dependencies = kv.Value.Deps };
                ModWorkshopMap.Replace(entries, hostHas);
                GD.Print($"{ModuleInit.LogTag} Client parsed sidecar: hostHas={hostHas}, {parsed.Count} mapping(s)");
            }
            // 没有 sidecar：保留 map，不打日志（buffered/重发会反复触发）
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{ModuleInit.LogTag} ClientInitialInfoSidecarPatch failed: {ex}");
        }
    }
}