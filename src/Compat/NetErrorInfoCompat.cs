using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;

namespace AutoModSubscriber.Compat;

/// <summary>
/// 封装不同游戏版本中 <see cref="NetErrorInfo"/> 附加断连信息的读取方式。
/// </summary>
internal static class NetErrorInfoCompat
{
    // 0.108 起，游戏把原来的私有字段改成了公开只读属性。
    private static readonly PropertyInfo? ConnectionExtraInfoProperty =
        AccessTools.Property(typeof(NetErrorInfo), "ConnectionExtraInfo");

    // 0.107.1 及更早版本使用私有字段，保留此回退以使同一份 MOD DLL 向前兼容。
    private static readonly FieldInfo? ConnectionExtraInfoField =
        AccessTools.Field(typeof(NetErrorInfo), "_connectionExtraInfo");

    internal static ConnectionFailureExtraInfo? GetConnectionExtraInfo(NetErrorInfo info)
    {
        object boxed = info;

        if (ConnectionExtraInfoProperty?.GetValue(boxed) is ConnectionFailureExtraInfo propertyValue)
            return propertyValue;

        return ConnectionExtraInfoField?.GetValue(boxed) as ConnectionFailureExtraInfo;
    }
}
