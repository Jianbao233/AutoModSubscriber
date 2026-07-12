using System;
using System.Linq;
using System.Reflection;
using AutoModSubscriber.Compat;
using AutoModSubscriber.Subscribe;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace AutoModSubscriber.UI;

/// <summary>
/// 拦截 NErrorPopup.Create(NetErrorInfo)：当原因是 ModMismatch 时，
/// 阻止原 popup，转而打开 AutoSubscribeDialog。
///
/// 用 [HarmonyPatch] + TargetMethod 反射定位方法，避免 typeof(NetErrorInfo)
/// 在编译/运行期引用漂移导致 PatchAll 静默失败。
/// </summary>
[HarmonyPatch]
internal static class ClientModMismatchInterceptPatch
{
    /// <summary>
    /// 外部 mod（如联机大厅）可注册此回调来接管 ModMismatch 弹窗的 UI。
    /// 返回 true 表示已处理，AMS 不再弹自己的 AutoSubscribeDialog；
    /// 返回 false 或 null 时 AMS 使用默认 UI。
    /// </summary>
    public static Func<ConnectionFailureExtraInfo, bool>? ExternalDialogHandler;

    [HarmonyPrepare]
    public static bool Prepare(MethodBase? original)
    {
        if (original == null)
        {
            try
            {
                var target = ResolveTarget();
                if (target == null)
                {
                    GD.PrintErr($"{ModuleInit.LogTag} InterceptPatch.Prepare: NErrorPopup.Create(NetErrorInfo) not found, ModMismatch hook disabled");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"{ModuleInit.LogTag} InterceptPatch.Prepare error: {ex}");
                return false;
            }
        }
        return true;
    }

    [HarmonyTargetMethod]
    public static MethodBase? TargetMethod()
    {
        return ResolveTarget();
    }

    private static MethodBase? ResolveTarget()
    {
        var popupType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.CommonUi.NErrorPopup")
                        ?? typeof(NErrorPopup);
        var infoType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Multiplayer.NetErrorInfo")
                       ?? typeof(NetErrorInfo);
        if (popupType == null || infoType == null) return null;

        var candidates = popupType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "Create")
            .ToList();

        var byParams = candidates.FirstOrDefault(m =>
        {
            var ps = m.GetParameters();
            return ps.Length == 1 && ps[0].ParameterType.FullName == infoType.FullName;
        });
        return byParams;
    }

    [HarmonyPostfix]
    public static void Postfix(object info, ref object? __result)
    {
        try
        {
            if (info is not NetErrorInfo netInfo) return;

            NetError reason;
            try { reason = netInfo.GetReason(); }
            catch (Exception innerEx)
            {
                GD.PrintErr($"{ModuleInit.LogTag} GetReason failed: {innerEx.Message}");
                return;
            }

            if (reason != NetError.ModMismatch) return;

            // JML 的可选联机功能标记需要由 JML 展示专用说明。
            // 此时保留 JML Prefix 已生成的 popup，不再替换为自动订阅对话框。
            if (JmcModLibCompat.ShouldHandle(netInfo))
            {
                GD.Print($"{ModuleInit.LogTag} ModMismatch routed to JmcModLib");
                return;
            }

            var extra = TryGetExtraInfo(netInfo);
            if (extra == null)
            {
                GD.Print($"{ModuleInit.LogTag} ModMismatch but no extraInfo, keep vanilla popup");
                return;
            }

            // 外部 mod（如联机大厅）注册了 UI 接管回调时，优先让它处理
            if (ExternalDialogHandler != null && ExternalDialogHandler(extra))
            {
                // 外部已处理，释放原 popup 并退出
                if (__result is Godot.Node extNode)
                {
                    try { extNode.QueueFree(); } catch { /* ignore */ }
                    __result = null;
                }
                return;
            }

            // 把原 popup 释放掉，避免双弹窗
            if (__result is Godot.Node node)
            {
                try { node.QueueFree(); } catch { /* ignore */ }
                __result = null;
            }

            ShowDialogDeferred(extra);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{ModuleInit.LogTag} ClientModMismatchInterceptPatch failed: {ex}");
        }
    }

    internal static ConnectionFailureExtraInfo? TryGetExtraInfo(NetErrorInfo info)
    {
        try
        {
            return NetErrorInfoCompat.GetConnectionExtraInfo(info);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{ModuleInit.LogTag} reading ConnectionExtraInfo failed: {ex}");
            return null;
        }
    }

    private static int _dialogOpenInflight;
    private const string DialogNodeName = "AutoSubscribeDialog";

    internal static void ShowDialogDeferred(ConnectionFailureExtraInfo extra)
    {
        // 短时去重：同一次 ModMismatch 可能由多个调用方各调一次 NErrorPopup.Create
        if (System.Threading.Interlocked.Exchange(ref _dialogOpenInflight, 1) == 1) return;

        Callable.From(() =>
        {
            try
            {
                var tree = Engine.GetMainLoop() as SceneTree;
                if (tree?.Root == null)
                {
                    GD.PrintErr($"{ModuleInit.LogTag} SceneTree.Root null, cannot show dialog");
                    return;
                }

                // 二次保险：场景树里已存在 AutoSubscribeDialog 时跳过
                var existing = tree.Root.FindChild(DialogNodeName, true, false);
                if (existing != null) return;

                var dlg = AutoSubscribeDialog.Build(extra);
                dlg.Name = DialogNodeName;
                tree.Root.AddChild(dlg);
                GD.Print($"{ModuleInit.LogTag} AutoSubscribeDialog opened");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"{ModuleInit.LogTag} ShowDialogDeferred failed: {ex}");
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _dialogOpenInflight, 0);
            }
        }).CallDeferred();
    }
}
