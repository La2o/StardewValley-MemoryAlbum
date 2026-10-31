using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;

namespace MemoryAlbum.Framework;

/// <summary>Renders what the player sees (without the HUD or menus) into album photos and polaroid exports.</summary>
/// <remarks>This uses the same approach as the game's map screenshot feature: temporarily set <see cref="Game1.takingMapScreenshot"/>, which hides the HUD, menus and overlays, and redraw the world into a render target.</remarks>
public class PhotoCamera
{
    /*********
    ** Fields
    *********/
    private readonly IMonitor Monitor;
    private readonly IReflectionHelper Reflection;
    private readonly ITranslationHelper Translation;
    private readonly AlbumStore Store;
    private readonly Func<ModConfig> GetConfig;

    /// <summary>The color of the date stamp burned into photos, like an old film camera.</summary>
    private static readonly Color StampColor = new(255, 148, 48);

    /// <summary>The background color of polaroid exports.</summary>
    private static readonly Color PolaroidPaper = new(248, 245, 236);

    /// <summary>The text color on polaroid exports.</summary>
    private static readonly Color PolaroidInk = new(64, 52, 42);


    /*********
    ** Accessors
    *********/
    /// <summary>Whether a photo is being rendered right now. Overlays should check this and skip drawing.</summary>
    public static bool IsCapturing { get; private set; }


    /*********
    ** Public methods
    *********/
    public PhotoCamera(IMonitor monitor, IReflectionHelper reflection, ITranslationHelper translation, AlbumStore store, Func<ModConfig> getConfig)
    {
        this.Monitor = monitor;
        this.Reflection = reflection;
        this.Translation = translation;
        this.Store = store;
        this.GetConfig = getConfig;
    }

    /// <summary>Get whether the game is in a state where a photo can be rendered.</summary>
    public static bool CanCapture()
    {
        return
            Context.IsWorldReady
            && Game1.currentLocation is not null
            && Game1.gameMode == Game1.playingGameMode
            && !Game1.game1.takingMapScreenshot
            && !Game1.newDay
            && !Game1.showingEndOfNightStuff;
    }

    /// <summary>Take a photo of the current view and add it to the album.</summary>
    /// <param name="caption">The initial caption.</param>
    /// <param name="milestone">The milestone key, if it's an automatic milestone photo.</param>
    /// <param name="posedWith">The internal name of the character who posed for it, if any.</param>
    /// <param name="effects">Whether to play the shutter sound and flash (if enabled in the config).</param>
    /// <returns>Returns the new photo, or <c>null</c> if it couldn't be taken.</returns>
    public Photo? TakePhoto(string caption = "", string? milestone = null, string? posedWith = null, bool effects = true)
    {
        if (!CanCapture())
            return null;

        GameLocation location = Game1.currentLocation;
        int width = Game1.viewport.Width;
        int height = Game1.viewport.Height;
        if (width <= 0 || height <= 0)
            return null;

        Photo photo = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Width = width,
            Height = height,
            PlayerId = Game1.player.UniqueMultiplayerID,
            Year = Game1.year,
            Season = Game1.season,
            Day = Game1.dayOfMonth,
            TimeOfDay = Game1.timeOfDay,
            DaysPlayed = (int)Game1.stats.DaysPlayed,
            TakenUtc = DateTime.UtcNow,
            LocationName = location.NameOrUniqueName,
            LocationDisplayName = location.DisplayName,
            Weather = GetWeather(location),
            Festival = Game1.CurrentEvent is { isFestival: true } festival ? festival.FestivalName : null,
            Caption = caption,
            Milestone = milestone,
            PosedWith = posedWith,
            People = GetVisiblePeople(location)
        };

        try
        {
            using Texture2D image = this.RenderView(width, height);
            if (this.GetConfig().StampDate)
                this.DrawDateStamp(image, photo);

            string path = this.Store.GetNewFilePath($"Y{photo.Year}-{photo.Season}-{photo.Day:00}_{photo.TimeOfDay:0000}_{photo.LocationName}");
            using (FileStream stream = File.Create(path))
                image.SaveAsPng(stream, width, height);

            photo.FileName = Path.GetFileName(path);
            this.Store.Add(photo);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't take a photo: {ex}", LogLevel.Error);
            return null;
        }

        if (effects && this.GetConfig().ShutterEffects)
        {
            Game1.flashAlpha = 1f;
            Game1.playSound("cameraNoise");
        }

        return photo;
    }

    /// <summary>Save a polaroid-style image of a photo, with its caption and date written underneath.</summary>
    /// <returns>Returns the saved file path, or <c>null</c> if it failed.</returns>
    public string? ExportPolaroid(Photo photo)
    {
        Texture2D? texture = this.Store.GetTexture(photo);
        if (texture is null)
            return null;

        int border = Math.Max(16, texture.Width / 20);
        int strip = Math.Max(96, texture.Height * 2 / 7);
        int width = texture.Width + border * 2;
        int height = texture.Height + border + strip;

        GraphicsDevice device = Game1.graphics.GraphicsDevice;
        RenderTarget2D? target = null;
        try
        {
            target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(target);
            device.Clear(PolaroidPaper);

            SpriteBatch batch = Game1.spriteBatch;
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            batch.Draw(texture, new Rectangle(border, border, texture.Width, texture.Height), Color.White);

            // caption
            string caption = string.IsNullOrWhiteSpace(photo.Caption) ? photo.LocationDisplayName : photo.Caption;
            SpriteFont captionFont = Game1.dialogueFont;
            Vector2 captionSize = captionFont.MeasureString(caption);
            float captionScale = Math.Min(strip * 0.34f / captionFont.LineSpacing, (width - border * 2) / Math.Max(1, captionSize.X));
            Vector2 captionPos = new((width - captionSize.X * captionScale) / 2, texture.Height + border + strip * 0.18f);
            batch.DrawString(captionFont, caption, captionPos, PolaroidInk, 0, Vector2.Zero, captionScale, SpriteEffects.None, 1);

            // date
            string date = PhotoText.GetDateLine(photo, this.Translation);
            SpriteFont dateFont = Game1.smallFont;
            Vector2 dateSize = dateFont.MeasureString(date);
            float dateScale = Math.Min(strip * 0.2f / dateFont.LineSpacing, (width - border * 2) / Math.Max(1, dateSize.X));
            Vector2 datePos = new((width - dateSize.X * dateScale) / 2, captionPos.Y + captionFont.LineSpacing * captionScale + strip * 0.06f);
            batch.DrawString(dateFont, date, datePos, PolaroidInk * 0.7f, 0, Vector2.Zero, dateScale, SpriteEffects.None, 1);
            batch.End();

            device.SetRenderTarget(null);

            string path = this.Store.GetNewFilePath(Path.GetFileNameWithoutExtension(photo.FileName) + "_polaroid", ModIds.PolaroidFolderName);
            using Texture2D opaque = CopyOpaque(target);
            using FileStream stream = File.Create(path);
            opaque.SaveAsPng(stream, width, height);
            return path;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't export a polaroid for {photo.FileName}: {ex}", LogLevel.Error);
            return null;
        }
        finally
        {
            device.SetRenderTarget(null);
            target?.Dispose();
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Redraw the current view into a new texture, without the HUD or menus.</summary>
    private Texture2D RenderView(int width, int height)
    {
        IReflectedField<RenderTarget2D?> lightmapField = this.Reflection.GetField<RenderTarget2D?>(typeof(Game1), "_lightmap");
        IReflectedMethod allocateLightmap = this.Reflection.GetMethod(typeof(Game1), "allocateLightmap");
        IReflectedMethod draw = this.Reflection.GetMethod(Game1.game1, "_draw");

        GraphicsDevice device = Game1.graphics.GraphicsDevice;
        bool wasHudShown = Game1.displayHUD;
        float baseZoom = Game1.options.baseZoomLevel;
        RenderTarget2D? lightmap = lightmapField.GetValue();
        RenderTarget2D? target = null;

        IsCapturing = true;
        Game1.game1.takingMapScreenshot = true;
        Game1.options.baseZoomLevel = 1f;
        lightmapField.SetValue(null);
        try
        {
            allocateLightmap.Invoke(width, height);

            target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            draw.Invoke(Game1.currentGameTime, target);
            device.SetRenderTarget(null);

            return CopyOpaque(target);
        }
        finally
        {
            device.SetRenderTarget(null);
            target?.Dispose();

            lightmapField.GetValue()?.Dispose();
            lightmapField.SetValue(lightmap);

            Game1.options.baseZoomLevel = baseZoom;
            Game1.game1.takingMapScreenshot = false;
            Game1.displayHUD = wasHudShown;
            IsCapturing = false;
        }
    }

    /// <summary>Draw the date into the bottom-right corner of a photo, like an old film camera.</summary>
    private void DrawDateStamp(Texture2D image, Photo photo)
    {
        GraphicsDevice device = Game1.graphics.GraphicsDevice;
        using RenderTarget2D target = new(device, image.Width, image.Height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

        string text = PhotoText.GetStamp(photo, this.Translation);
        SpriteFont font = Game1.smallFont;
        float scale = image.Height * 0.045f / font.LineSpacing;
        Vector2 size = font.MeasureString(text) * scale;
        float margin = image.Height * 0.03f;
        Vector2 position = new(image.Width - size.X - margin, image.Height - size.Y - margin);
        Vector2 shadowOffset = new(Math.Max(1, scale * 1.5f));

        device.SetRenderTarget(target);
        SpriteBatch batch = Game1.spriteBatch;
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
        batch.Draw(image, Vector2.Zero, Color.White);
        batch.End();
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        batch.DrawString(font, text, position + shadowOffset, new Color(70, 24, 0) * 0.55f, 0, Vector2.Zero, scale, SpriteEffects.None, 1);
        batch.DrawString(font, text, position, StampColor, 0, Vector2.Zero, scale, SpriteEffects.None, 1);
        batch.End();
        device.SetRenderTarget(null);

        Color[] data = new Color[image.Width * image.Height];
        target.GetData(data);
        ForceOpaque(data);
        image.SetData(data);
    }

    /// <summary>Copy a render target into a regular texture with every pixel fully opaque.</summary>
    /// <remarks>Some blend steps leave partially transparent pixels in the render target, which would look washed out in a PNG viewer.</remarks>
    private static Texture2D CopyOpaque(RenderTarget2D target)
    {
        Color[] data = new Color[target.Width * target.Height];
        target.GetData(data);
        ForceOpaque(data);

        Texture2D texture = new(Game1.graphics.GraphicsDevice, target.Width, target.Height);
        texture.SetData(data);
        return texture;
    }

    private static void ForceOpaque(Color[] data)
    {
        for (int i = 0; i < data.Length; i++)
            data[i].A = byte.MaxValue;
    }

    /// <summary>Get the weather shown in a location.</summary>
    private static PhotoWeather GetWeather(GameLocation location)
    {
        if (!location.IsOutdoors)
            return PhotoWeather.Indoors;
        if (Game1.IsGreenRainingHere(location))
            return PhotoWeather.GreenRain;
        if (Game1.IsLightningHere(location))
            return PhotoWeather.Storm;
        if (Game1.IsRainingHere(location))
            return PhotoWeather.Rain;
        if (Game1.IsSnowingHere(location))
            return PhotoWeather.Snow;
        if (location.IsDebrisWeatherHere())
            return PhotoWeather.Wind;
        return PhotoWeather.Sun;
    }

    /// <summary>Get the display names of the villagers, children and pets visible on screen.</summary>
    private static List<string> GetVisiblePeople(GameLocation location)
    {
        Rectangle view = new(Game1.viewport.X, Game1.viewport.Y, Game1.viewport.Width, Game1.viewport.Height);

        IEnumerable<NPC> characters = location.characters;
        if (Game1.CurrentEvent?.actors is { } actors)
            characters = characters.Concat(actors);

        return characters
            .Where(npc => IsPhotographable(npc) && view.Intersects(npc.GetBoundingBox()))
            .Select(npc => npc.displayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Get whether a character counts as a person (or pet) for photo tags and posing.</summary>
    public static bool IsPhotographable(NPC npc)
    {
        if (npc.IsInvisible)
            return false;

        return npc is Child or Pet || (npc.IsVillager && npc.CanSocialize);
    }
}
