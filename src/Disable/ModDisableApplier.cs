using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;

namespace AutoModSubscriber.Disable;

/// <summary>
/// 把用户勾选的"我有 host 没有"的 mod 写为禁用，并落盘 settings.save。
/// 由于 ModManager._settings 是 private static，用 AccessTools 反射读。
/// </summary>
public static class ModDisableApplier
{
    public sealed record DisableResult(List<string> Succeeded, List<string> Failed);

    public static DisableResult Apply(IEnumerable<string> manifestIds)
    {
        var succ = new List<string>();
        var fail = new List<string>();

        ModSettings? settings = TryGetModSettings();
        if (settings == null)
        {
            foreach (var id in manifestIds) fail.Add(id);
            GD.PrintErr($"{ModuleInit.LogTag} Apply: ModManager._settings is null");
            return new DisableResult(succ, fail);
        }

        var loadedMods = ModManager.Mods;

        foreach (var id in manifestIds)
        {
            try
            {
                var mod = loadedMods.FirstOrDefault(m => m.manifest?.id == id);
                ModSource source = mod?.modSource ?? ModSource.ModsDirectory;

                var existing = settings.ModList.FirstOrDefault(m => m.Id == id);
                if (existing != null)
                {
                    existing.IsEnabled = false;
                    if (mod != null) existing.Source = mod.modSource;
                }
                else
                {
                    settings.ModList.Add(new SettingsSaveMod
                    {
                        Id = id,
                        Source = source,
                        IsEnabled = false,
                    });
                }
                succ.Add(id);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"{ModuleInit.LogTag} Apply disable for {id} failed: {ex}");
                fail.Add(id);
            }
        }

        try
        {
            SaveManager.Instance.SaveSettings();
            GD.Print($"{ModuleInit.LogTag} SaveSettings done, disabled {succ.Count} mod(s)");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{ModuleInit.LogTag} SaveSettings failed: {ex}");
            // 把成功列表降级为失败：内存改了但未持久化
            fail.AddRange(succ);
            succ.Clear();
        }

        return new DisableResult(succ, fail);
    }

    private static ModSettings? TryGetModSettings()
    {
        try
        {
            var field = AccessTools.Field(typeof(ModManager), "_settings");
            return field?.GetValue(null) as ModSettings;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{ModuleInit.LogTag} Reflecting ModManager._settings failed: {ex}");
            return null;
        }
    }
}