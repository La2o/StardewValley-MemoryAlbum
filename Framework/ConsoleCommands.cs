using System.Collections.Generic;
using System.Linq;
using System.Text;
using StardewModdingAPI;
using StardewValley;

namespace MemoryAlbum.Framework;

/// <summary>The SMAPI console commands for testing and troubleshooting.</summary>
public class ConsoleCommands
{
    private readonly IMonitor Monitor;
    private readonly AlbumStore Store;
    private readonly PhotoCamera Camera;
    private readonly MilestoneWatcher Milestones;

    public ConsoleCommands(IMonitor monitor, AlbumStore store, PhotoCamera camera, MilestoneWatcher milestones)
    {
        this.Monitor = monitor;
        this.Store = store;
        this.Camera = camera;
        this.Milestones = milestones;
    }

    public void Register(ICommandHelper commands)
    {
        commands.Add("album_snap", "Takes a photo of the current view.\n\nUsage: album_snap [caption]", this.Snap);
        commands.Add("album_list", "Lists the current player's photos.\n\nUsage: album_list", this.List);
        commands.Add("album_folder", "Opens the current save's album folder.\n\nUsage: album_folder", this.Folder);
        commands.Add("album_milestones", "Lists the milestones already photographed and waiting for a photo.\n\nUsage: album_milestones", this.ListMilestones);
        commands.Add("album_milestone", "Queues a milestone photo, which is taken the next time the scene is right.\n\nUsage: album_milestone <key>\n- key: a milestone key like DayOne, BestFriends:Abigail, Child:Jamie, House:1, or CommunityCenter.", this.QueueMilestone);
        commands.Add("album_reset_milestones", "Clears the current player's milestone state. Milestones already met are recorded again (without photos) the next time the save loads.\n\nUsage: album_reset_milestones", this.ResetMilestones);
    }

    private void Snap(string command, string[] args)
    {
        if (!this.AssertWorldReady())
            return;

        Photo? photo = this.Camera.TakePhoto(string.Join(" ", args));
        if (photo is not null)
            this.Monitor.Log($"Saved {photo.FileName} ({photo.Width}x{photo.Height}).", LogLevel.Info);
        else
            this.Monitor.Log("Couldn't take a photo right now.", LogLevel.Warn);
    }

    private void List(string command, string[] args)
    {
        if (!this.AssertWorldReady())
            return;

        List<Photo> photos = this.Store.GetPhotos(Game1.player);
        StringBuilder report = new();
        report.AppendLine($"{Game1.player.Name} has {photos.Count} photos in {this.Store.Folder}:");
        foreach (Photo photo in photos)
        {
            string tags = string.Join(", ", new[]
            {
                photo.Milestone is not null ? $"milestone {photo.Milestone}" : null,
                photo.PosedWith is not null ? $"posed with {photo.PosedWith}" : null,
                photo.Favorite ? "favorite" : null,
                photo.People.Any() ? $"people: {string.Join(", ", photo.People)}" : null
            }.Where(p => p is not null));

            report.AppendLine($"  Y{photo.Year} {photo.Season} {photo.Day} {photo.TimeOfDay:0000} @ {photo.LocationName}: \"{photo.Caption}\" ({photo.FileName}){(tags.Length > 0 ? $" [{tags}]" : "")}");
        }
        this.Monitor.Log(report.ToString(), LogLevel.Info);
    }

    private void Folder(string command, string[] args)
    {
        if (!this.AssertWorldReady())
            return;

        this.Monitor.Log($"The album folder is {this.Store.Folder}.", LogLevel.Info);
        this.Store.OpenFolder();
    }

    private void ListMilestones(string command, string[] args)
    {
        if (!this.AssertWorldReady())
            return;

        this.Milestones.GetStatus(Game1.player, out ISet<string> done, out ISet<string> pending);
        this.Monitor.Log(
            $"Waiting for a photo: {(pending.Any() ? string.Join(", ", pending.OrderBy(p => p)) : "none")}.\n"
            + $"Photographed or already met when the mod was installed: {(done.Any() ? string.Join(", ", done.OrderBy(p => p)) : "none")}.",
            LogLevel.Info
        );
    }

    private void QueueMilestone(string command, string[] args)
    {
        if (!this.AssertWorldReady())
            return;

        if (args.Length == 0)
        {
            this.Monitor.Log("Usage: album_milestone <key>", LogLevel.Error);
            return;
        }

        string key = string.Join(" ", args);
        this.Milestones.ForcePending(Game1.player, key);
        this.Monitor.Log($"Queued milestone {key} (\"{this.Milestones.GetCaption(Game1.player, key)}\"). It'll be photographed the next time the scene is right.", LogLevel.Info);
    }

    private void ResetMilestones(string command, string[] args)
    {
        if (!this.AssertWorldReady())
            return;

        this.Milestones.Reset(Game1.player);
        this.Milestones.InitializeIfNeeded(Game1.player);
        this.Monitor.Log("Reset the milestone state.", LogLevel.Info);
    }

    private bool AssertWorldReady()
    {
        if (Context.IsWorldReady)
            return true;

        this.Monitor.Log("You need to load a save first.", LogLevel.Error);
        return false;
    }
}
