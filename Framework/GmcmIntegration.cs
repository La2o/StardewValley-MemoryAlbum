using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using MemoryAlbum.Integrations;

namespace MemoryAlbum.Framework;

/// <summary>Registers the config with Generic Mod Config Menu, if it's installed.</summary>
public static class GmcmIntegration
{
    public static void Register(IModHelper helper, IManifest manifest, Func<ModConfig> getConfig, Action reset, Action save)
    {
        IGenericModConfigMenuApi? api = helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (api is null)
            return;

        ITranslationHelper i18n = helper.Translation;
        api.Register(manifest, reset, save);

        api.AddSectionTitle(manifest, () => i18n.Get("config.section.controls"));
        AddKeybind(api, manifest, i18n, "CameraKey", () => getConfig().CameraKey, value => getConfig().CameraKey = value);
        AddKeybind(api, manifest, i18n, "AlbumKey", () => getConfig().AlbumKey, value => getConfig().AlbumKey = value);

        api.AddSectionTitle(manifest, () => i18n.Get("config.section.camera"));
        AddBool(api, manifest, i18n, "StampDate", () => getConfig().StampDate, value => getConfig().StampDate = value);
        AddBool(api, manifest, i18n, "ShutterEffects", () => getConfig().ShutterEffects, value => getConfig().ShutterEffects = value);
        AddBool(api, manifest, i18n, "HideHudInPhotoMode", () => getConfig().HideHudInPhotoMode, value => getConfig().HideHudInPhotoMode = value);
        api.AddNumberOption(
            manifest,
            getValue: () => getConfig().MinHeartsToPose,
            setValue: value => getConfig().MinHeartsToPose = (int)value,
            name: () => i18n.Get("config.MinHeartsToPose.name"),
            tooltip: () => i18n.Get("config.MinHeartsToPose.tooltip"),
            min: 0,
            max: 10,
            interval: 1
        );

        api.AddSectionTitle(manifest, () => i18n.Get("config.section.memories"));
        AddBool(api, manifest, i18n, "AutoCaptureMilestones", () => getConfig().AutoCaptureMilestones, value => getConfig().AutoCaptureMilestones = value);
        AddBool(api, manifest, i18n, "ShowHudOnMemory", () => getConfig().ShowHudOnMemory, value => getConfig().ShowHudOnMemory = value);
        AddBool(api, manifest, i18n, "OnThisDayReminders", () => getConfig().OnThisDayReminders, value => getConfig().OnThisDayReminders = value);
    }

    private static void AddBool(IGenericModConfigMenuApi api, IManifest manifest, ITranslationHelper i18n, string option, Func<bool> getValue, Action<bool> setValue)
    {
        api.AddBoolOption(
            manifest,
            getValue: getValue,
            setValue: setValue,
            name: () => i18n.Get($"config.{option}.name"),
            tooltip: () => i18n.Get($"config.{option}.tooltip")
        );
    }

    private static void AddKeybind(IGenericModConfigMenuApi api, IManifest manifest, ITranslationHelper i18n, string option, Func<KeybindList> getValue, Action<KeybindList> setValue)
    {
        api.AddKeybindList(
            manifest,
            getValue: getValue,
            setValue: setValue,
            name: () => i18n.Get($"config.{option}.name"),
            tooltip: () => i18n.Get($"config.{option}.tooltip")
        );
    }
}
