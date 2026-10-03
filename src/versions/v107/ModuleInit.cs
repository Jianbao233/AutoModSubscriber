using System;
using Godot;
using HarmonyLib;

namespace AutoModSubscriber;

/// <summary>
/// 实现入口（游戏 v0.107.x 分支）。
///
/// 入口由 ModVersionLoader 启动器反射调用，因此这里**不再**标注
/// [ModInitializer]（那是根目录启动器的职责），也**不能**用
/// [ModuleInitializer]（会在程序集被触碰时抢先执行，与启动器形成双重初始化）。
///
/// 签名必须是 <c>public static void Initialize()</c> 且无参数 —— 启动器按此查找。
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