using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;

namespace AutoModSubscriber;

/// <summary>
/// Module initializer + Harmony entry point.
/// 加载顺序保险：[ModuleInitializer] 在程序集首次被任何 type 触碰时调用，
/// 这是 STS2 mod 加载流程中最稳定的 hook 点。
/// </summary>
public static class ModuleInit
{
    public const string ModId = "AutoModSubscriber";
    public const string LogTag = "[AutoModSubscriber]";

    private static bool _initialized;

    [ModuleInitializer]
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            GD.Print($"{LogTag} ModuleInit.Initialize() called");

            var asm = typeof(ModuleInit).Assembly;
            var harmony = new Harmony($"jianbao.{ModId}");
            harmony.PatchAll(asm);

            // 统计实际挂上的方法数
            int hookCount = 0;
            try
            {
                foreach (var method in Harmony.GetAllPatchedMethods())
                {
                    var info = Harmony.GetPatchInfo(method);
                    if (info == null) continue;
                    bool ours = false;
                    foreach (var p in info.Postfixes)
                        if (p.owner == harmony.Id) { ours = true; break; }
                    if (!ours)
                        foreach (var p in info.Prefixes)
                            if (p.owner == harmony.Id) { ours = true; break; }
                    if (ours) hookCount++;
                }
            }
            catch (Exception verifyEx)
            {
                GD.PrintErr($"{LogTag} Hook verification failed: {verifyEx}");
            }
            GD.Print($"{LogTag} Harmony PatchAll done: hooked {hookCount} method(s)");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{LogTag} ModuleInit failed: {ex}");
        }
    }
}