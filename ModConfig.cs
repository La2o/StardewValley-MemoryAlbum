using StardewModdingAPI.Utilities;

namespace MemoryAlbum;

/// <summary>The mod configuration.</summary>
public class ModConfig
{
    public KeybindList CameraKey { get; set; } = KeybindList.Parse("O");
    public KeybindList AlbumKey { get; set; } = KeybindList.Parse("L");
    public bool StampDate { get; set; } = true;
    public bool ShutterEffects { get; set; } = true;
    public bool HideHudInPhotoMode { get; set; } = true;
    public bool AutoCaptureMilestones { get; set; } = true;
    public bool ShowHudOnMemory { get; set; } = true;
    public bool OnThisDayReminders { get; set; } = true;
    public int MinHeartsToPose { get; set; } = 2;
}
