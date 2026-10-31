using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using System.Text.Json;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Locations;

namespace MemoryAlbum.Framework;

/// <summary>Detects life milestones (weddings, children, the Community Center, etc.) and photographs them automatically.</summary>
/// <remarks>
/// Each milestone has a key like <c>Child:Penny Jr</c>. When a milestone is first met it's added to the player's pending list,
/// then photographed the next time the scene is right (e.g. the child is on screen). Weddings and the Community Center ceremony
/// are photographed during the cutscene itself.
/// </remarks>
public class MilestoneWatcher
{
    /*********
    ** Fields
    *********/
    /// <summary>The event ID for the Community Center completion ceremony.</summary>
    private const string CommunityCenterEventId = "191393";

    /// <summary>The event ID for the Joja warehouse completion ceremony.</summary>
    private const string JojaEventId = "502261";

    /// <summary>How long to wait after arriving somewhere before taking a milestone photo, in ticks, so the screen has faded in.</summary>
    private const int SettleTicks = 90;

    private readonly IMonitor Monitor;
    private readonly ITranslationHelper Translation;
    private readonly PhotoCamera Camera;
    private readonly Func<ModConfig> GetConfig;
    private readonly Func<bool> IsPhotoModeActive;
    private readonly PerScreen<ScreenState> States = new(() => new ScreenState());


    /*********
    ** Public methods
    *********/
    public MilestoneWatcher(IMonitor monitor, ITranslationHelper translation, PhotoCamera camera, Func<ModConfig> getConfig, Func<bool> isPhotoModeActive)
    {
        this.Monitor = monitor;
        this.Translation = translation;
        this.Camera = camera;
        this.GetConfig = getConfig;
        this.IsPhotoModeActive = isPhotoModeActive;
    }

    /// <summary>Record the milestones already met in a save the first time it's loaded, so they're not all photographed at once.</summary>
    public void InitializeIfNeeded(Farmer player)
    {
        if (player.modData.ContainsKey(ModIds.InitializedKey))
            return;

        HashSet<string> met = this.GetMetMilestones(player, includeSlowChecks: true);

        // a brand new farm gets its day one photo
        if (Game1.stats.DaysPlayed <= 1)
            met.Remove(MilestoneKey.DayOne);

        SetSet(player, ModIds.MilestonesDoneKey, met);
        player.modData[ModIds.InitializedKey] = "1";
        this.Monitor.Log($"Recorded {met.Count} milestones already met by {player.Name}, so they won't be photographed.");
    }

    /// <summary>Reset the per-screen state, e.g. when a new day starts or the player returns to the title screen.</summary>
    public void ResetState()
    {
        this.States.Value = new ScreenState();
    }

    /// <summary>Note that the player just arrived somewhere, so milestone photos wait for the screen to fade in.</summary>
    public void OnWarped()
    {
        this.States.Value.TicksInLocation = 0;
    }

    /// <summary>Find newly met milestones and add them to the pending list.</summary>
    /// <param name="player">The player to check.</param>
    /// <param name="includeSlowChecks">Whether to include checks which are too slow to run often.</param>
    public void CheckForNewMilestones(Farmer player, bool includeSlowChecks)
    {
        if (!player.modData.ContainsKey(ModIds.InitializedKey))
            return;

        HashSet<string> done = GetSet(player, ModIds.MilestonesDoneKey);
        HashSet<string> pending = GetSet(player, ModIds.MilestonesPendingKey);
        bool autoCapture = this.GetConfig().AutoCaptureMilestones;

        bool changed = false;
        foreach (string key in this.GetMetMilestones(player, includeSlowChecks))
        {
            if (done.Contains(key) || pending.Contains(key))
                continue;

            // when disabled, still record milestones so they don't all trigger when it's re-enabled
            if (autoCapture)
            {
                pending.Add(key);
                this.Monitor.Log($"Milestone reached: {key}. Waiting for a good moment to photograph it.");
            }
            else
                done.Add(key);
            changed = true;
        }

        if (changed)
        {
            SetSet(player, ModIds.MilestonesDoneKey, done);
            SetSet(player, ModIds.MilestonesPendingKey, pending);
        }
    }

    /// <summary>Update the milestone state and take any milestone photos whose moment has come. Call once per tick.</summary>
    public void Update(Farmer player)
    {
        ScreenState state = this.States.Value;
        state.TicksInLocation++;

        if (!this.GetConfig().AutoCaptureMilestones || !player.modData.ContainsKey(ModIds.InitializedKey))
            return;

        this.UpdateEventCapture(player, state);

        if (Game1.ticks % 15 == 0 && this.IsSceneSettled(state) && Context.IsPlayerFree && !this.IsPhotoModeActive())
            this.TryCapturePending(player);
    }

    /// <summary>Add a milestone to the pending list, for testing.</summary>
    public void ForcePending(Farmer player, string key)
    {
        HashSet<string> done = GetSet(player, ModIds.MilestonesDoneKey);
        HashSet<string> pending = GetSet(player, ModIds.MilestonesPendingKey);
        done.Remove(key);
        pending.Add(key);
        SetSet(player, ModIds.MilestonesDoneKey, done);
        SetSet(player, ModIds.MilestonesPendingKey, pending);
    }

    /// <summary>Get the milestones already photographed (or skipped) and waiting to be photographed.</summary>
    public void GetStatus(Farmer player, out ISet<string> done, out ISet<string> pending)
    {
        done = GetSet(player, ModIds.MilestonesDoneKey);
        pending = GetSet(player, ModIds.MilestonesPendingKey);
    }

    /// <summary>Clear the player's milestone state, so it's re-initialized next time.</summary>
    public void Reset(Farmer player)
    {
        player.modData.Remove(ModIds.MilestonesDoneKey);
        player.modData.Remove(ModIds.MilestonesPendingKey);
        player.modData.Remove(ModIds.InitializedKey);
    }

    /// <summary>Get the caption for a milestone photo.</summary>
    public string GetCaption(Farmer player, string key)
    {
        MilestoneKey.Parse(key, out string type, out string? subject);

        string name = subject is not null ? GetDisplayName(subject) : "";
        string farm = Game1.MasterPlayer.farmName.Value;

        return type switch
        {
            MilestoneKey.House => this.Translation.Get($"caption.milestone.House.{Math.Clamp(int.TryParse(subject, out int level) ? level : 1, 1, 3)}"),
            MilestoneKey.BestFriends when subject is not null && player.friendshipData.TryGetValue(subject, out Friendship friendship) && friendship.IsDating()
                => this.Translation.Get("caption.milestone.Sweethearts", new { name }),
            _ => this.Translation.Get($"caption.milestone.{type}", new { name, farm, year = subject })
        };
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get every milestone the player has currently met.</summary>
    private HashSet<string> GetMetMilestones(Farmer player, bool includeSlowChecks)
    {
        HashSet<string> met = new() { MilestoneKey.DayOne };

        // new years
        for (int year = 2; year <= Game1.year; year++)
            met.Add(MilestoneKey.Build(MilestoneKey.NewYear, year.ToString()));

        // relationships
        foreach ((string name, Friendship friendship) in player.friendshipData.Pairs)
        {
            if (friendship.IsEngaged() || friendship.IsMarried())
                met.Add(MilestoneKey.Build(MilestoneKey.Engaged, name));
            if (friendship.IsMarried())
                met.Add(MilestoneKey.Build(MilestoneKey.Wedding, name));
            if (friendship.Points >= 8 * NPC.friendshipPointsPerHeartLevel && Game1.getCharacterFromName(name) is { } npc && npc.CanSocialize)
                met.Add(MilestoneKey.Build(MilestoneKey.BestFriends, name));
        }
        if (player.team.GetSpouse(player.UniqueMultiplayerID) is { } spouseId && Game1.GetPlayer(spouseId) is { } spouseFarmer)
            met.Add(MilestoneKey.Build(MilestoneKey.Wedding, spouseFarmer.Name));

        // family
        foreach (Child child in player.getChildren())
            met.Add(MilestoneKey.Build(MilestoneKey.Child, child.Name));
        foreach (Pet pet in Utility.getAllPets())
            met.Add(MilestoneKey.Build(MilestoneKey.Pet, pet.Name));
        for (int level = 1; level <= player.HouseUpgradeLevel; level++)
            met.Add(MilestoneKey.Build(MilestoneKey.House, level.ToString()));

        // town
        if (Game1.getFarm().greenhouseUnlocked.Value)
            met.Add(MilestoneKey.Greenhouse);
        if (player.eventsSeen.Contains(CommunityCenterEventId))
            met.Add(MilestoneKey.CommunityCenter);
        if (player.eventsSeen.Contains(JojaEventId))
            met.Add(MilestoneKey.Joja);

        // perfection
        if (includeSlowChecks && Utility.percentGameComplete() >= 1f)
            met.Add(MilestoneKey.Perfection);

        return met;
    }

    /// <summary>Photograph milestone cutscenes (weddings and the Community Center or Joja ceremony) while they play.</summary>
    private void UpdateEventCapture(Farmer player, ScreenState state)
    {
        Event? ev = Game1.CurrentEvent;
        if (ev is null)
        {
            state.Event = null;
            return;
        }

        if (!ReferenceEquals(ev, state.Event))
        {
            state.Event = ev;
            state.EventTicks = 0;
            state.KissTick = -1;
            state.EventCaptured = false;
        }
        state.EventTicks++;

        if (state.EventCaptured)
            return;

        string? key = GetEventMilestoneKey(player, ev);
        if (key is null || GetSet(player, ModIds.MilestonesDoneKey).Contains(key))
            return;

        bool ready;
        if (ev.isWedding)
        {
            // the kiss is the shot
            if (state.KissTick < 0 && GetCurrentCommand(ev)?.StartsWith("showKissFrame", StringComparison.OrdinalIgnoreCase) == true)
                state.KissTick = state.EventTicks;

            ready = state.KissTick >= 0
                ? state.EventTicks - state.KissTick >= 30
                : state.EventTicks >= 60 * 25; // fallback for modded wedding scripts without a kiss
        }
        else
        {
            // once the speech starts, everyone's gathered
            ready = (state.EventTicks >= 60 * 6 && Game1.dialogueUp) || state.EventTicks >= 60 * 20;
        }

        if (ready && !Game1.fadeToBlack && !Game1.globalFade && Game1.fadeToBlackAlpha <= 0.05f && this.Capture(player, key))
            state.EventCaptured = true;
    }

    /// <summary>Photograph the first pending milestone whose scene is ready, if any.</summary>
    private void TryCapturePending(Farmer player)
    {
        HashSet<string> pending = GetSet(player, ModIds.MilestonesPendingKey);
        if (pending.Count == 0)
            return;

        foreach (string key in pending)
        {
            if (this.IsSceneReady(player, key))
            {
                this.Capture(player, key);
                return;
            }
        }
    }

    /// <summary>Take a milestone photo and mark it done.</summary>
    private bool Capture(Farmer player, string key)
    {
        string caption = this.GetCaption(player, key);
        Photo? photo = this.Camera.TakePhoto(caption, milestone: key);
        if (photo is null)
            return false;

        HashSet<string> done = GetSet(player, ModIds.MilestonesDoneKey);
        HashSet<string> pending = GetSet(player, ModIds.MilestonesPendingKey);
        done.Add(key);
        pending.Remove(key);
        SetSet(player, ModIds.MilestonesDoneKey, done);
        SetSet(player, ModIds.MilestonesPendingKey, pending);

        this.Monitor.Log($"Photographed milestone {key}.");
        if (this.GetConfig().ShowHudOnMemory)
            Game1.addHUDMessage(new HUDMessage(this.Translation.Get("hud.milestone", new { caption }), HUDMessage.achievement_type));

        return true;
    }

    /// <summary>Get whether the current scene suits a milestone photo.</summary>
    private bool IsSceneReady(Farmer player, string key)
    {
        GameLocation location = Game1.currentLocation;
        MilestoneKey.Parse(key, out string type, out string? subject);

        switch (type)
        {
            case MilestoneKey.DayOne:
            case MilestoneKey.Perfection:
                return true;

            case MilestoneKey.NewYear:
                return location.IsFarm && location.IsOutdoors;

            case MilestoneKey.House:
                return location is FarmHouse house && house.HasOwner && house.OwnerId == player.UniqueMultiplayerID;

            case MilestoneKey.Greenhouse:
                return location.IsGreenhouse;

            case MilestoneKey.CommunityCenter:
                return location is CommunityCenter;

            case MilestoneKey.Joja:
                return location.Name == "Town";

            case MilestoneKey.Engaged:
            case MilestoneKey.Wedding:
            case MilestoneKey.BestFriends:
            case MilestoneKey.Child:
            case MilestoneKey.Pet:
                return subject is not null && IsOnScreen(location, subject);

            default:
                return false;
        }
    }

    /// <summary>Get whether the screen has finished fading in after arriving somewhere.</summary>
    private bool IsSceneSettled(ScreenState state)
    {
        return state.TicksInLocation >= SettleTicks && !Game1.fadeToBlack && !Game1.globalFade && Game1.fadeToBlackAlpha <= 0.05f;
    }

    /// <summary>Get the milestone key photographed by a cutscene, if it's one.</summary>
    private static string? GetEventMilestoneKey(Farmer player, Event ev)
    {
        if (ev.isWedding)
        {
            if (player.team.GetSpouse(player.UniqueMultiplayerID) is { } spouseId && Game1.GetPlayer(spouseId) is { } spouseFarmer)
                return MilestoneKey.Build(MilestoneKey.Wedding, spouseFarmer.Name);
            return player.spouse is { Length: > 0 } spouse
                ? MilestoneKey.Build(MilestoneKey.Wedding, spouse)
                : null;
        }

        return ev.id switch
        {
            CommunityCenterEventId => MilestoneKey.CommunityCenter,
            JojaEventId => MilestoneKey.Joja,
            _ => null
        };
    }

    /// <summary>Get the event command being run, if any.</summary>
    private static string? GetCurrentCommand(Event ev)
    {
        int index = ev.CurrentCommand;
        return ev.eventCommands is { } commands && index >= 0 && index < commands.Length
            ? commands[index]
            : null;
    }

    /// <summary>Get whether a character or player with the given name is visible on screen.</summary>
    private static bool IsOnScreen(GameLocation location, string name)
    {
        Rectangle view = new(Game1.viewport.X, Game1.viewport.Y, Game1.viewport.Width, Game1.viewport.Height);
        view.Inflate(-64, -64); // not just peeking in at the edge

        foreach (NPC npc in location.characters)
        {
            if (npc.Name == name && !npc.IsInvisible && view.Intersects(npc.GetBoundingBox()))
                return true;
        }

        foreach (Farmer farmer in location.farmers)
        {
            if (farmer.Name == name && view.Intersects(farmer.GetBoundingBox()))
                return true;
        }

        return false;
    }

    /// <summary>Get the display name for a character or player.</summary>
    private static string GetDisplayName(string name)
    {
        if (Game1.getCharacterFromName(name) is { } npc)
            return npc.displayName;
        return NPC.GetDisplayName(name);
    }

    private static HashSet<string> GetSet(Farmer player, string key)
    {
        if (player.modData.TryGetValue(key, out string? raw) && !string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                return JsonSerializer.Deserialize<HashSet<string>>(raw) ?? new HashSet<string>();
            }
            catch
            {
                // invalid data, start over
            }
        }

        return new HashSet<string>();
    }

    private static void SetSet(Farmer player, string key, ISet<string> values)
    {
        player.modData[key] = JsonSerializer.Serialize(values.OrderBy(p => p));
    }


    /*********
    ** Private types
    *********/
    /// <summary>The milestone state for one screen.</summary>
    private class ScreenState
    {
        public int TicksInLocation;
        public Event? Event;
        public int EventTicks;
        public int KissTick = -1;
        public bool EventCaptured;
    }
}

/// <summary>Builds and parses milestone keys.</summary>
public static class MilestoneKey
{
    public const string DayOne = "DayOne";
    public const string NewYear = "NewYear";
    public const string Engaged = "Engaged";
    public const string Wedding = "Wedding";
    public const string Child = "Child";
    public const string House = "House";
    public const string Pet = "Pet";
    public const string BestFriends = "BestFriends";
    public const string Greenhouse = "Greenhouse";
    public const string CommunityCenter = "CommunityCenter";
    public const string Joja = "Joja";
    public const string Perfection = "Perfection";

    public static string Build(string type, string subject)
    {
        return $"{type}:{subject}";
    }

    public static void Parse(string key, out string type, out string? subject)
    {
        int index = key.IndexOf(':');
        if (index < 0)
        {
            type = key;
            subject = null;
        }
        else
        {
            type = key[..index];
            subject = key[(index + 1)..];
        }
    }
}
