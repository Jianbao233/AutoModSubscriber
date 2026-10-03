using System;
using Godot;
using HarmonyLib;
using AutoModSubscriber.Protocol;
using AutoModSubscriber.UI;

namespace AutoModSubscriber;

/// <summary>
/// 实现入口。
///
/// 注意：本类**不再**标注 <c>[ModInitializer]</c>。版本分发机制下，游戏加载的是
/// 根目录的 <c>AutoModSubscriber.dll</c>（ModVersionLoader 启动器），启动器按当前
/// 游戏版本挑中本实现 DLL 后，反射调用这里的 <see cref="Initialize"/>。
///
/// 因此本方法必须是 <c>public static void</c> 且无参数 —— 启动器按此签名查找。
/// </summary>
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
            GD.Print($"{LogTag} version bundle: {SelectedVersionDir()} selected by loader");

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

    /// <summary>
    /// 启动器选中的版本目录名（由 ModVersionLoader 通过环境变量传入）。
    /// 启动器自身无法可靠地写游戏日志（依赖 Godot 原生绑定），所以由实现来记录。
    /// </summary>
    private static string SelectedVersionDir()
    {
        try
        {
            return System.Environment.GetEnvironmentVariable("AMS_LOADER_SELECTED_VERSION") ?? "<direct load>";
        }
        catch
        {
            return "<unknown>";
        }
    }
}