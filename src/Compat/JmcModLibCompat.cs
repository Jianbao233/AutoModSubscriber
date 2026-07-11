using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;

namespace AutoModSubscriber.Compat;

/// <summary>
/// 提供对 JmcModLib 可选联机功能不匹配路由接口的可选调用。
/// </summary>
/// <remarks>
/// AutoModSubscriber 不直接引用 JmcModLib，确保未安装 JML 时仍可独立加载。
/// 找到接口后会缓存强类型委托，后续判断不再经过反射调用。
/// </remarks>
internal static class JmcModLibCompat
{
    private const string RouterTypeName = "JmcModLib.Multiplayer.OptionalNetworkMismatch";
    private static readonly object ResolveLock = new();
    private static Func<NetErrorInfo, bool>? _shouldHandle;
    private static bool _loggedFailure;

    internal static bool ShouldHandle(NetErrorInfo info)
    {
        Func<NetErrorInfo, bool>? handler = _shouldHandle ?? ResolveHandler();
        if (handler == null)
            return false;

        try
        {
            return handler(info);
        }
        catch (Exception ex)
        {
            LogFailureOnce($"calling {RouterTypeName}.ShouldHandle failed", ex);
            return false;
        }
    }

    private static Func<NetErrorInfo, bool>? ResolveHandler()
    {
        lock (ResolveLock)
        {
            if (_shouldHandle != null)
                return _shouldHandle;

            // JML 的运行时 DLL 由 Bootstrap 加载，文件名可能是 JmcModLib.Runtime.dll，
            // 因此按已加载程序集中的类型全名查找，不依赖程序集文件名。
            Type? routerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(static assembly => assembly.GetType(RouterTypeName, throwOnError: false))
                .FirstOrDefault(static type => type != null);
            if (routerType == null)
                return null;

            MethodInfo? method = routerType.GetMethod(
                "ShouldHandle",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(NetErrorInfo)],
                modifiers: null);
            if (method == null || method.ReturnType != typeof(bool))
                return null;

            try
            {
                _shouldHandle = method.CreateDelegate<Func<NetErrorInfo, bool>>();
                return _shouldHandle;
            }
            catch (Exception ex)
            {
                LogFailureOnce($"binding {RouterTypeName}.ShouldHandle failed", ex);
                return null;
            }
        }
    }

    private static void LogFailureOnce(string message, Exception ex)
    {
        if (_loggedFailure)
            return;

        _loggedFailure = true;
        GD.PrintErr($"{ModuleInit.LogTag} {message}: {ex}");
    }
}
