using Godot;

namespace AutoModSubscriber.UI;

/// <summary>
/// 弹窗使用的本地化文案。
/// 运行时根据 OS.GetLocale() 自动切中文/英文，不依赖游戏的 LocManager
/// （避免引入 RitsuLib/STS2 的本地化键空间）。
/// </summary>
internal static class DialogStrings
{
    public static bool IsChinese
    {
        get
        {
            try
            {
                // 优先用游戏内语言（玩家在设置里选的）
                var lang = MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language;
                if (!string.IsNullOrEmpty(lang))
                    return lang!.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase);
            }
            catch { /* LocManager 可能尚未初始化，回退 */ }

            try
            {
                var loc = OS.GetLocale() ?? "";
                return loc.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    private static string Pick(string zh, string en) => IsChinese ? zh : en;

    public static string Title => Pick(
        "联机 Mod 不匹配",
        "Multiplayer Mod Mismatch");

    public static string SectionMissingOnLocal => Pick(
        "房主有但你没有的 Mod",
        "Mods the host has that you are missing");

    public static string SectionMissingOnHost => Pick(
        "你有但房主没有的 Mod",
        "Mods you have that the host doesn't");

    public static string BtnSubscribeAll => Pick(
        "全部自动订阅",
        "Subscribe all");

    public static string BtnDisableSelected => Pick(
        "禁用勾选项",
        "Disable selected");

    public static string BtnClose => Pick(
        "关闭",
        "Close");

    public static string BtnOpenWorkshop => Pick(
        "在工坊搜索",
        "Open Workshop");

    public static string StatePending       => Pick("待处理",    "Pending");
    public static string StateSubscribing   => Pick("订阅中",    "Subscribing");
    public static string StateDownloading   => Pick("下载中",    "Downloading");
    public static string StateWaitingInstall=> Pick("安装中",    "Installing");
    public static string StateInstalled     => Pick("已安装",    "Installed");
    public static string StateFailed        => Pick("失败",      "Failed");
    public static string StateTimedOut      => Pick("超时",      "Timed out");
    public static string StateNoWorkshopId  => Pick("无 workshopId", "No workshopId");

    public static string HintHostNotInstalled => Pick(
        "房主未安装 AutoModSubscriber，无法自动订阅。\n请在 Steam 工坊手动搜索缺失的 mod。",
        "The host does not have AutoModSubscriber installed, so auto-subscribe is unavailable.\nPlease search the missing mods on the Steam Workshop manually.");

    public static string HintHostHasModNoWorkshop => Pick(
        "房主已安装 AutoModSubscriber，但缺失的 mod 不在 Steam 工坊（可能是本地版本/手动安装），\n无法自动订阅，请联系房主或手动安装。",
        "The host has AutoModSubscriber, but the missing mods are not on Steam Workshop (likely local/manual installs).\nAuto-subscribe is unavailable; please ask the host or install them manually.");

    public static string HintSteamUnavailable => Pick(
        "Steam 未正确初始化，无法订阅。请确认 Steam 客户端在运行。",
        "Steam is not initialized. Make sure the Steam client is running.");

    public static string HintSubscribeDone => Pick(
        "已订阅 {0} 个，失败 {1} 个。请关闭并重启游戏后再尝试加入房间。",
        "Subscribed {0} mod(s), {1} failed. Please restart the game before trying to join again.");

    public static string HintDisableDone => Pick(
        "已禁用 {0} 个 mod，失败 {1} 个。重启游戏后生效。",
        "Disabled {0} mod(s), {1} failed. Restart the game for the changes to take effect.");

    public static string EmptySection => Pick("（无）", "(none)");

    public static string FormatStatus(Subscribe.SubscribeJobState s) => s switch
    {
        Subscribe.SubscribeJobState.Pending        => StatePending,
        Subscribe.SubscribeJobState.Subscribing    => StateSubscribing,
        Subscribe.SubscribeJobState.Downloading    => StateDownloading,
        Subscribe.SubscribeJobState.WaitingInstall => StateWaitingInstall,
        Subscribe.SubscribeJobState.Installed      => StateInstalled,
        Subscribe.SubscribeJobState.Failed         => StateFailed,
        Subscribe.SubscribeJobState.TimedOut       => StateTimedOut,
        _ => "?",
    };
}