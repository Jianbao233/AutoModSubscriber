using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using AutoModSubscriber.Protocol;
using AutoModSubscriber.UI;

namespace AutoModSubscriber;

[ModInitializer(nameof(Initialize))]
public static class ModuleInit
{
    public const string ModId = "AutoModSubscriber";
    public const string LogTag = "[AutoModSubscriber]";

    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            GD.Print($"{LogTag} ModuleInit.Initialize() called");

            var harmony = new Harmony($"jianbao.{ModId}");
            var patchTypes = new[]
            {
                typeof(HostInitialInfoSidecarPatch),
                typeof(ClientInitialInfoSidecarPatch),
                typeof(ClientModMismatchInterceptPatch)
            };

            int hookCount = 0;
            foreach (var patchType in patchTypes)
            {
                try
                {
                    harmony.CreateClassProcessor(patchType).Patch();
                    hookCount++;
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"{LogTag} Failed to apply {patchType.Name}: {ex}");
                }
            }

            GD.Print($"{LogTag} Harmony initialization complete: {hookCount}/{patchTypes.Length} patch class(es) applied");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{LogTag} ModuleInit failed: {ex}");
        }
    }
}