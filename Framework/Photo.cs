using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using StardewValley;

namespace MemoryAlbum.Framework;

/// <summary>The weather when a photo was taken.</summary>
public enum PhotoWeather
{
    Sun,
    Rain,
    Storm,
    Snow,
    GreenRain,
    Wind,
    Indoors
}

/// <summary>A photo in the album, and what was happening when it was taken.</summary>
public class Photo
{
    /*********
    ** Accessors
    *********/
    /// <summary>A unique ID for the photo.</summary>
    public string Id { get; set; } = "";

    /// <summary>The image file name, relative to the album folder.</summary>
    public string FileName { get; set; } = "";

    /// <summary>The image size, in pixels.</summary>
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>The unique multiplayer ID of the player who took it.</summary>
    public long PlayerId { get; set; }

    /// <summary>The in-game date and time it was taken.</summary>
    public int Year { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Season Season { get; set; }

    public int Day { get; set; }
    public int TimeOfDay { get; set; }

    /// <summary>The number of in-game days played when it was taken, for sorting.</summary>
    public int DaysPlayed { get; set; }

    /// <summary>The real time it was taken.</summary>
    public DateTime TakenUtc { get; set; }

    /// <summary>The internal and display name of the location it was taken in.</summary>
    public string LocationName { get; set; } = "";
    public string LocationDisplayName { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PhotoWeather Weather { get; set; }

    /// <summary>The festival name, if it was taken at a festival.</summary>
    public string? Festival { get; set; }

    /// <summary>The player's caption.</summary>
    public string Caption { get; set; } = "";

    /// <summary>The milestone key, if it's an automatic milestone photo.</summary>
    public string? Milestone { get; set; }

    /// <summary>The internal name of the villager, child or pet who posed for the photo, if any.</summary>
    public string? PosedWith { get; set; }

    /// <summary>The display names of the villagers, children and pets visible in the photo.</summary>
    public List<string> People { get; set; } = new();

    /// <summary>Whether the player marked it as a favorite.</summary>
    public bool Favorite { get; set; }


    /*********
    ** Public methods
    *********/
    /// <summary>Get the season index, for date formatting.</summary>
    public int GetSeasonIndex()
    {
        return (int)this.Season;
    }
}

/// <summary>The contents of an album's index file.</summary>
public class AlbumIndex
{
    /// <summary>The format version, for migrations.</summary>
    public int Format { get; set; } = 1;

    /// <summary>The photos in the album, in no particular order.</summary>
    public List<Photo> Photos { get; set; } = new();
}
