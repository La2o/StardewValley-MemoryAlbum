using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using System.Text.Json;
using StardewModdingAPI;
using StardewValley;

namespace MemoryAlbum.Framework;

/// <summary>Reads and writes the current save's album: the photo files and their index.</summary>
/// <remarks>Albums live in the game's screenshot folder (not the save folder), so they're easy to find and share and don't bloat save backups. Each farm gets its own folder.</remarks>
public class AlbumStore
{
    /*********
    ** Fields
    *********/
    private readonly IMonitor Monitor;

    /// <summary>The loaded index, if a save is loaded.</summary>
    private AlbumIndex? Index;

    /// <summary>The loaded photo textures by photo ID.</summary>
    private readonly Dictionary<string, Texture2D?> Textures = new();


    /*********
    ** Accessors
    *********/
    /// <summary>The absolute path to the current save's album folder, if a save is loaded.</summary>
    public string? Folder { get; private set; }


    /*********
    ** Public methods
    *********/
    public AlbumStore(IMonitor monitor)
    {
        this.Monitor = monitor;
    }

    /// <summary>Load the album for the current save, if it's not already loaded.</summary>
    public void LoadForCurrentSave()
    {
        string folder = GetFolderForCurrentSave();
        if (this.Folder == folder && this.Index is not null)
            return;

        this.Unload();
        this.Folder = folder;

        string indexPath = Path.Combine(folder, ModIds.IndexFileName);
        try
        {
            this.Index = File.Exists(indexPath)
                ? JsonSerializer.Deserialize<AlbumIndex>(File.ReadAllText(indexPath)) ?? new AlbumIndex()
                : new AlbumIndex();
        }
        catch (Exception ex)
        {
            // keep the broken file, so the player doesn't lose their captions if they fix it by hand
            string backupPath = indexPath + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            this.Monitor.Log($"Couldn't read the album index at {indexPath}, so starting a new one. The old file was moved to {backupPath}.\n{ex}", LogLevel.Error);
            try
            {
                File.Move(indexPath, backupPath);
            }
            catch
            {
                // not much else we can do
            }
            this.Index = new AlbumIndex();
        }

        // drop entries whose image was deleted outside the game
        int removed = this.Index.Photos.RemoveAll(photo => !File.Exists(this.GetImagePath(photo)));
        if (removed > 0)
        {
            this.Monitor.Log($"Removed {removed} album entries whose image files no longer exist.");
            this.Save();
        }
    }

    /// <summary>Unload the album, e.g. when returning to the title screen.</summary>
    public void Unload()
    {
        this.ReleaseTextures();
        this.Index = null;
        this.Folder = null;
    }

    /// <summary>Get the photos taken by a player, oldest first.</summary>
    public List<Photo> GetPhotos(Farmer player)
    {
        if (this.Index is null)
            return new List<Photo>();

        return this.Index.Photos
            .Where(photo => photo.PlayerId == player.UniqueMultiplayerID)
            .OrderBy(photo => photo.DaysPlayed)
            .ThenBy(photo => photo.TimeOfDay)
            .ThenBy(photo => photo.TakenUtc)
            .ToList();
    }

    /// <summary>Get the absolute path for a new photo file.</summary>
    /// <param name="baseName">The file name without its extension.</param>
    /// <param name="subfolder">The subfolder within the album folder, if any.</param>
    public string GetNewFilePath(string baseName, string? subfolder = null)
    {
        string folder = this.RequireFolder();
        if (subfolder is not null)
            folder = Path.Combine(folder, subfolder);
        Directory.CreateDirectory(folder);

        // keep separators (unlike SaveGame.FilterFileName), so names like Y1-Spring-02_0600_Town stay readable
        string name = new(baseName.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
        string path = Path.Combine(folder, name + ".png");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{name}_{i}.png");
        return path;
    }

    /// <summary>Get the absolute path to a photo's image file.</summary>
    public string GetImagePath(Photo photo)
    {
        return Path.Combine(this.RequireFolder(), photo.FileName);
    }

    /// <summary>Add a photo whose image file was already saved.</summary>
    public void Add(Photo photo)
    {
        this.RequireIndex().Photos.Add(photo);
        this.Save();
    }

    /// <summary>Delete a photo and its image file.</summary>
    public void Delete(Photo photo)
    {
        this.RequireIndex().Photos.Remove(photo);
        this.Save();

        this.ReleaseTexture(photo);
        try
        {
            File.Delete(this.GetImagePath(photo));
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't delete photo file {photo.FileName}: {ex.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Write the index to disk.</summary>
    public void Save()
    {
        if (this.Index is null || this.Folder is null)
            return;

        try
        {
            Directory.CreateDirectory(this.Folder);
            string path = Path.Combine(this.Folder, ModIds.IndexFileName);
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(this.Index, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't save the album index: {ex}", LogLevel.Error);
        }
    }

    /// <summary>Get the texture for a photo, loading it if needed.</summary>
    /// <returns>Returns the texture, or <c>null</c> if the image couldn't be loaded.</returns>
    public Texture2D? GetTexture(Photo photo)
    {
        if (this.Textures.TryGetValue(photo.Id, out Texture2D? texture))
            return texture;

        try
        {
            using FileStream stream = File.OpenRead(this.GetImagePath(photo));
            texture = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't load photo {photo.FileName}: {ex.Message}", LogLevel.Warn);
            texture = null;
        }

        this.Textures[photo.Id] = texture;
        return texture;
    }

    /// <summary>Unload every loaded texture except those for the given photos.</summary>
    public void ReleaseTexturesExcept(IEnumerable<Photo> keep)
    {
        HashSet<string> keepIds = new(keep.Select(p => p.Id));
        foreach (string id in this.Textures.Keys.Where(id => !keepIds.Contains(id)).ToArray())
        {
            this.Textures[id]?.Dispose();
            this.Textures.Remove(id);
        }
    }

    /// <summary>Unload every loaded texture.</summary>
    public void ReleaseTextures()
    {
        this.ReleaseTexturesExcept(Array.Empty<Photo>());
    }

    /// <summary>Open the album folder in the system file browser.</summary>
    public void OpenFolder()
    {
        string folder = this.RequireFolder();
        Directory.CreateDirectory(folder);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't open the album folder ({folder}): {ex.Message}", LogLevel.Warn);
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get the album folder for the current save.</summary>
    private static string GetFolderForCurrentSave()
    {
        string farmName = SaveGame.FilterFileName(Game1.MasterPlayer.farmName.Value);
        if (string.IsNullOrWhiteSpace(farmName))
            farmName = "Farm";

        return Path.Combine(Game1.game1.GetScreenshotFolder(), ModIds.RootFolderName, $"{farmName}_{Game1.uniqueIDForThisGame}");
    }

    private void ReleaseTexture(Photo photo)
    {
        if (this.Textures.Remove(photo.Id, out Texture2D? texture))
            texture?.Dispose();
    }

    private string RequireFolder()
    {
        return this.Folder ?? throw new InvalidOperationException("No album is loaded.");
    }

    private AlbumIndex RequireIndex()
    {
        return this.Index ?? throw new InvalidOperationException("No album is loaded.");
    }
}
