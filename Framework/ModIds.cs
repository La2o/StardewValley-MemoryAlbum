namespace MemoryAlbum.Framework;

/// <summary>Identifiers used for mod data and files. These must stay stable across versions, since they're stored in saves.</summary>
public static class ModIds
{
    /// <summary>The mod's unique ID (must match the manifest).</summary>
    public const string ModId = "ygsnaffy.MemoryAlbum";

    /// <summary>The prefix for mod data keys stored on the player.</summary>
    public const string ModDataPrefix = ModId + "/";

    /// <summary>The player mod data key which lists the milestones already captured (or skipped).</summary>
    public const string MilestonesDoneKey = ModDataPrefix + "MilestonesDone";

    /// <summary>The player mod data key which lists the milestones waiting for a good moment to be photographed.</summary>
    public const string MilestonesPendingKey = ModDataPrefix + "MilestonesPending";

    /// <summary>The player mod data key set once milestones already met in an existing save have been recorded.</summary>
    public const string InitializedKey = ModDataPrefix + "Initialized";

    /// <summary>The folder name, under the game's screenshot folder, which contains every album.</summary>
    public const string RootFolderName = "MemoryAlbum";

    /// <summary>The file name for an album's photo index.</summary>
    public const string IndexFileName = "album.json";

    /// <summary>The subfolder name for exported polaroids.</summary>
    public const string PolaroidFolderName = "Polaroids";
}
