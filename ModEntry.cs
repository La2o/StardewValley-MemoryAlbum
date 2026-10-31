using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Extensions;
using MemoryAlbum.Framework;

namespace MemoryAlbum;

/// <summary>The mod entry point.</summary>
public class ModEntry : Mod
{
    /*********
    ** Fields
    *********/
    private ModConfig Config = null!;
    private AlbumStore Store = null!;
    private PhotoCamera Camera = null!;
    private PhotoMode PhotoMode = null!;
    private MilestoneWatcher Milestones = null!;


    /*********
    ** Public methods
    *********/
    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();
        this.Store = new AlbumStore(this.Monitor);
        this.Camera = new PhotoCamera(this.Monitor, helper.Reflection, helper.Translation, this.Store, () => this.Config);
        this.PhotoMode = new PhotoMode(helper.Translation, this.Camera, () => this.Config);
        this.Milestones = new MilestoneWatcher(this.Monitor, helper.Translation, this.Camera, () => this.Config, () => this.PhotoMode.IsActive);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        helper.Events.Player.Warped += this.OnWarped;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        helper.Events.Display.RenderedHud += (_, e) => this.PhotoMode.Draw(e.SpriteBatch);

        new ConsoleCommands(this.Monitor, this.Store, this.Camera, this.Milestones)
            .Register(helper.ConsoleCommands);
    }


    /*********
    ** Private methods
    *********/
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        GmcmIntegration.Register(
            this.Helper,
            this.ModManifest,
            getConfig: () => this.Config,
            reset: () => this.Config = new ModConfig(),
            save: () => this.Helper.WriteConfig(this.Config)
        );
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.Store.LoadForCurrentSave();
        this.Milestones.InitializeIfNeeded(Game1.player);
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.Store.LoadForCurrentSave();
        this.Milestones.ResetState();
        this.Milestones.CheckForNewMilestones(Game1.player, includeSlowChecks: true);

        if (this.Config.OnThisDayReminders)
            this.ShowOnThisDay();
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        this.PhotoMode.Update();

        if (!Context.IsWorldReady)
            return;

        if (e.IsMultipleOf(60))
            this.Milestones.CheckForNewMilestones(Game1.player, includeSlowChecks: false);
        this.Milestones.Update(Game1.player);
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.PhotoMode.Reset();
        this.Milestones.ResetState();
        this.Store.Unload();
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (e.IsLocalPlayer)
            this.Milestones.OnWarped();
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        // camera
        if (this.Config.CameraKey.JustPressed())
        {
            if (this.PhotoMode.IsActive)
            {
                this.PhotoMode.Exit();
                this.Helper.Input.SuppressActiveKeybinds(this.Config.CameraKey);
            }
            else if (PhotoMode.CanEnter())
            {
                this.PhotoMode.Enter();
                this.Helper.Input.SuppressActiveKeybinds(this.Config.CameraKey);
            }
            return;
        }

        if (this.PhotoMode.IsActive)
        {
            if (this.PhotoMode.HandleButton(e.Button))
                this.Helper.Input.Suppress(e.Button);
            return;
        }

        // album
        if (this.Config.AlbumKey.JustPressed())
        {
            if (Game1.activeClickableMenu is AlbumMenu album)
            {
                album.exitThisMenu();
                this.Helper.Input.SuppressActiveKeybinds(this.Config.AlbumKey);
            }
            else if (Context.IsPlayerFree)
            {
                Game1.activeClickableMenu = new AlbumMenu(this.Store, this.Camera, this.Helper.Translation, this.Config, Game1.player);
                this.Helper.Input.SuppressActiveKeybinds(this.Config.AlbumKey);
            }
        }
    }

    /// <summary>Show a reminder of a photo taken on this day in a previous year, if any.</summary>
    private void ShowOnThisDay()
    {
        Photo[] candidates = this.Store
            .GetPhotos(Game1.player)
            .Where(p => p.Season == Game1.season && p.Day == Game1.dayOfMonth && p.Year < Game1.year)
            .ToArray();
        if (candidates.Length == 0)
            return;

        // prefer the photos the player cares about most
        Photo[] best = candidates.Where(p => p.Favorite || p.Milestone is not null).ToArray();
        Photo photo = Game1.random.ChooseFrom(best.Length > 0 ? best : candidates);

        string caption = AlbumDrawing.Truncate(PhotoText.GetCaptionOrPlaceholder(photo, this.Helper.Translation), Game1.smallFont, 360);
        string message = this.Helper.Translation.Get("hud.on-this-day", new { years = Game1.year - photo.Year, caption, key = this.Config.AlbumKey.ToString() });
        Game1.addHUDMessage(new HUDMessage(message) { noIcon = true, timeLeft = HUDMessage.defaultTime * 1.5f });
    }
}
