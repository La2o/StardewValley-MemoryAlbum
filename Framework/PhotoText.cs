using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace MemoryAlbum.Framework;

/// <summary>Formats photo details for display.</summary>
public static class PhotoText
{
    /// <summary>Get the short date stamped onto the photo itself.</summary>
    public static string GetStamp(Photo photo, ITranslationHelper translation)
    {
        return translation.Get("stamp", new { season = GetSeasonName(photo), day = photo.Day, year = photo.Year });
    }

    /// <summary>Get the date line shown under a photo.</summary>
    public static string GetDate(Photo photo, ITranslationHelper translation)
    {
        return translation.Get("photo.date", new { season = GetSeasonName(photo), day = photo.Day, year = photo.Year });
    }

    /// <summary>Get the date and time line shown on polaroids and in the photo viewer.</summary>
    public static string GetDateLine(Photo photo, ITranslationHelper translation)
    {
        return translation.Get("photo.date-time", new
        {
            date = GetDate(photo, translation),
            time = Game1.getTimeOfDayString(photo.TimeOfDay)
        });
    }

    /// <summary>Get the caption, or a fallback if the player hasn't written one.</summary>
    public static string GetCaptionOrPlaceholder(Photo photo, ITranslationHelper translation)
    {
        return string.IsNullOrWhiteSpace(photo.Caption)
            ? translation.Get("photo.no-caption", new { location = photo.LocationDisplayName })
            : photo.Caption;
    }

    /// <summary>Get the details shown in the photo viewer, one per line.</summary>
    public static string GetDetails(Photo photo, ITranslationHelper translation)
    {
        string details = GetDateLine(photo, translation);

        details += "\n" + translation.Get("photo.location", new { location = photo.LocationDisplayName });
        if (photo.Festival is not null)
            details += "\n" + translation.Get("photo.festival", new { festival = photo.Festival });

        details += "\n" + translation.Get("photo.weather", new { weather = translation.Get($"weather.{photo.Weather}") });

        if (photo.People.Any())
            details += "\n" + translation.Get("photo.people", new { people = string.Join(", ", photo.People) });

        return details;
    }

    /// <summary>Get the localized season name for a photo.</summary>
    public static string GetSeasonName(Photo photo)
    {
        return Utility.getSeasonNameFromNumber(photo.GetSeasonIndex());
    }
}
