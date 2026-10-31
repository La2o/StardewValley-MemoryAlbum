using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Characters;
using StardewValley.Extensions;
using StardewValley.Menus;

namespace MemoryAlbum.Framework;

/// <summary>The camera viewfinder: hides the HUD, lets the player frame and take photos, and asks nearby villagers to pose.</summary>
public class PhotoMode
{
    /*********
    ** Fields
    *********/
    /// <summary>How close a character must be to ask them to pose, in tiles.</summary>
    private const float PoseRange = 2.5f;

    /// <summary>How long a posed photo counts down before it's taken, in ticks.</summary>
    private const int PoseCountdownTicks = 90;

    private readonly ITranslationHelper Translation;
    private readonly PhotoCamera Camera;
    private readonly Func<ModConfig> GetConfig;
    private readonly PerScreen<State> States = new(() => new State());

    private State Current => this.States.Value;


    /*********
    ** Accessors
    *********/
    /// <summary>Whether the viewfinder is open for the current player.</summary>
    public bool IsActive => this.Current.Active;


    /*********
    ** Public methods
    *********/
    public PhotoMode(ITranslationHelper translation, PhotoCamera camera, Func<ModConfig> getConfig)
    {
        this.Translation = translation;
        this.Camera = camera;
        this.GetConfig = getConfig;
    }

    /// <summary>Get whether the player can open the viewfinder right now.</summary>
    public static bool CanEnter()
    {
        if (!Context.IsWorldReady || Game1.player.UsingTool || Game1.currentMinigame is not null)
            return false;

        // cutscenes and festivals are worth photographing too
        return Context.IsPlayerFree || (Game1.eventUp && Game1.activeClickableMenu is null or DialogueBox);
    }

    /// <summary>Open the viewfinder.</summary>
    public void Enter()
    {
        State state = this.Current;
        if (state.Active)
            return;

        state.Active = true;
        state.HidHud = false;
        if (this.GetConfig().HideHudInPhotoMode && Game1.displayHUD)
        {
            Game1.displayHUD = false;
            state.HidHud = true;
        }

        Game1.playSound("smallSelect");
    }

    /// <summary>Close the viewfinder.</summary>
    public void Exit()
    {
        State state = this.Current;
        if (!state.Active)
            return;

        if (state.HidHud && !Game1.displayHUD)
            Game1.displayHUD = true;

        this.States.Value = new State();
        Game1.playSound("smallSelect");
    }

    /// <summary>Discard all state, e.g. when returning to the title screen.</summary>
    public void Reset()
    {
        this.States.ResetAllScreens();
    }

    /// <summary>Update the viewfinder state. Call once per tick.</summary>
    public void Update()
    {
        State state = this.Current;
        if (!state.Active)
            return;

        if (!Context.IsWorldReady)
        {
            this.States.Value = new State();
            return;
        }

        // close if the player opened a menu (dialogue is fine, since that's how villagers answer)
        if (Game1.activeClickableMenu is not null and not DialogueBox)
        {
            this.Exit();
            return;
        }

        state.Nearby = Game1.eventUp ? null : FindPoseSubject();

        // posed photo countdown
        if (state.PoseSubject is { } subject && state.PoseTicksLeft >= 0)
        {
            subject.Halt();
            subject.faceDirection(Game1.down);
            subject.movementPause = Math.Max(subject.movementPause, 500);

            if (--state.PoseTicksLeft < 0)
            {
                this.Snap(state.PoseCaption, posedWith: subject.Name);
                state.PoseSubject = null;
                state.PoseCaption = "";
            }
        }
    }

    /// <summary>Handle a button press while the viewfinder is open.</summary>
    /// <returns>Returns whether the button was handled and should be suppressed.</returns>
    public bool HandleButton(SButton button)
    {
        State state = this.Current;
        if (!state.Active || Game1.activeClickableMenu is not null)
            return false;

        if (IsExitButton(button))
        {
            this.Exit();
            return true;
        }

        // wait for the posed photo
        if (state.PoseSubject is not null)
            return button.IsUseToolButton() || button.IsActionButton();

        if (button.IsUseToolButton())
        {
            this.Snap();
            return true;
        }

        if (button.IsActionButton())
        {
            if (state.Nearby is { } npc)
                this.AskToPose(npc);
            else
                this.Snap();
            return true;
        }

        return false;
    }

    /// <summary>Draw the viewfinder overlay.</summary>
    public void Draw(SpriteBatch b)
    {
        State state = this.Current;
        if (!state.Active || PhotoCamera.IsCapturing)
            return;

        Rectangle screen = new(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height);

        // frame corners
        const int margin = 32;
        const int length = 72;
        const int thickness = 4;
        Rectangle frame = new(screen.X + margin, screen.Y + margin, screen.Width - margin * 2, screen.Height - margin * 2);
        DrawCorners(b, frame, length, thickness, Color.Black * 0.35f, offset: 2);
        DrawCorners(b, frame, length, thickness, Color.White * 0.9f, offset: 0);

        // recording dot + title
        bool blink = Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 1400 < 900;
        Vector2 titlePos = new(frame.X + 28, frame.Y + 22);
        if (blink)
            b.Draw(Game1.staminaRect, new Rectangle((int)titlePos.X, (int)titlePos.Y + 8, 16, 16), new Color(230, 60, 50));
        Utility.drawTextWithShadow(b, this.Translation.Get("viewfinder.title"), Game1.smallFont, titlePos + new Vector2(28, 0), Color.White);

        // date stamp preview
        if (this.GetConfig().StampDate)
        {
            string stamp = this.Translation.Get("stamp", new { season = Utility.getSeasonNameFromNumber(Game1.seasonIndex), day = Game1.dayOfMonth, year = Game1.year });
            Vector2 size = Game1.smallFont.MeasureString(stamp);
            Utility.drawTextWithShadow(b, stamp, Game1.smallFont, new Vector2(frame.Right - size.X - 28, frame.Bottom - size.Y - 22), new Color(255, 148, 48));
        }

        // posed countdown
        if (state.PoseSubject is not null && state.PoseTicksLeft >= 0)
        {
            int seconds = (int)Math.Ceiling(state.PoseTicksLeft / 30f);
            string countdown = seconds > 0 ? seconds.ToString() : this.Translation.Get("viewfinder.cheese");
            SpriteText.drawStringWithScrollCenteredAt(b, countdown, screen.Center.X, screen.Height / 4);
        }

        // hints (not over a villager's reply)
        if (Game1.activeClickableMenu is not null)
            return;
        string hint = this.GetHint(state);
        Vector2 hintSize = Game1.smallFont.MeasureString(hint);
        Rectangle hintBox = new((int)(screen.Center.X - hintSize.X / 2 - 20), (int)(frame.Bottom - hintSize.Y - 36), (int)hintSize.X + 40, (int)hintSize.Y + 16);
        b.Draw(Game1.staminaRect, hintBox, Color.Black * 0.45f);
        Utility.drawTextWithShadow(b, hint, Game1.smallFont, new Vector2(hintBox.X + 20, hintBox.Y + 8), Color.White);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Take a photo now.</summary>
    private void Snap(string caption = "", string? posedWith = null)
    {
        Photo? photo = this.Camera.TakePhoto(caption, posedWith: posedWith);
        Game1.addHUDMessage(photo is not null
            ? new HUDMessage(this.Translation.Get("hud.photo-saved", new { key = this.GetConfig().AlbumKey.ToString() })) { noIcon = true }
            : new HUDMessage(this.Translation.Get("hud.photo-failed"), HUDMessage.error_type)
        );
    }

    /// <summary>Ask a villager, child or pet to pose for a photo.</summary>
    private void AskToPose(NPC npc)
    {
        string name = npc.displayName;

        switch (npc)
        {
            case Pet:
                this.BeginPose(npc, this.Translation.Get("caption.pose.pet", new { name }), Character.heartEmote);
                return;

            case Child:
                this.BeginPose(npc, this.Translation.Get("caption.pose.child", new { name }), Character.happyEmote);
                return;
        }

        bool isSpouse = Game1.player.spouse == npc.Name;
        int hearts = Game1.player.getFriendshipHeartLevelForNPC(npc.Name);
        npc.faceGeneralDirection(Game1.player.getStandingPosition());

        if (!isSpouse && hearts < this.GetConfig().MinHeartsToPose)
        {
            Game1.DrawDialogue(new Dialogue(npc, null, this.GetRandomLine("pose.decline")));
            return;
        }

        string tier = isSpouse ? "spouse" : hearts >= 8 ? "best" : "friend";
        string caption = this.Translation.Get($"caption.pose.{tier}", new { name });
        int emote = tier == "friend" ? Character.happyEmote : Character.heartEmote;

        Game1.DrawDialogue(new Dialogue(npc, null, this.GetRandomLine($"pose.agree.{tier}")));
        Game1.afterDialogues = () => this.BeginPose(npc, caption, emote);
    }

    /// <summary>Start the countdown for a posed photo.</summary>
    private void BeginPose(NPC npc, string caption, int emote)
    {
        State state = this.Current;
        if (!state.Active)
            return;

        state.PoseSubject = npc;
        state.PoseCaption = caption;
        state.PoseTicksLeft = PoseCountdownTicks;

        npc.Halt();
        npc.faceDirection(Game1.down);
        npc.movementPause = PoseCountdownTicks * 1000 / 60 + 1000;
        npc.doEmote(emote);

        Game1.player.Halt();
        Game1.player.faceDirection(Game1.down);
        Game1.player.freezePause = PoseCountdownTicks * 1000 / 60;

        Game1.playSound("dwop");
    }

    /// <summary>Get a random line from a set of numbered translations (like <c>pose.decline.1</c> to <c>pose.decline.3</c>).</summary>
    private string GetRandomLine(string prefix)
    {
        string[] lines = Enumerable.Range(1, 9)
            .Select(i => this.Translation.Get($"{prefix}.{i}"))
            .Where(p => p.HasValue())
            .Select(p => p.ToString())
            .ToArray();

        return lines.Length > 0
            ? Game1.random.ChooseFrom(lines)
            : "...";
    }

    /// <summary>Get the hint text shown at the bottom of the viewfinder.</summary>
    private string GetHint(State state)
    {
        string input = Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse ? "gamepad" : "mouse";

        if (state.PoseSubject is not null)
            return this.Translation.Get("viewfinder.hint.posing", new { name = state.PoseSubject.displayName });

        string hint = this.Translation.Get($"viewfinder.hint.snap.{input}");
        if (state.Nearby is { } npc)
            hint += "   " + this.Translation.Get($"viewfinder.hint.pose.{input}", new { name = npc.displayName });
        hint += "   " + this.Translation.Get($"viewfinder.hint.exit.{input}", new { key = this.GetConfig().CameraKey.ToString() });

        return hint;
    }

    /// <summary>Find the closest villager, child or pet who can be asked to pose.</summary>
    private static NPC? FindPoseSubject()
    {
        Vector2 playerTile = Game1.player.Tile;

        return Game1.currentLocation?.characters
            .Where(PhotoCamera.IsPhotographable)
            .Select(npc => new { npc, distance = Vector2.Distance(npc.Tile, playerTile) })
            .Where(p => p.distance <= PoseRange)
            .OrderBy(p => p.distance)
            .Select(p => p.npc)
            .FirstOrDefault();
    }

    /// <summary>Get whether a button should close the viewfinder.</summary>
    private static bool IsExitButton(SButton button)
    {
        if (button is SButton.Escape or SButton.ControllerB or SButton.ControllerBack)
            return true;

        return button.TryGetKeyboard(out var key) && Game1.options.doesInputListContain(Game1.options.menuButton, key);
    }

    /// <summary>Draw four L-shaped corner brackets around an area.</summary>
    private static void DrawCorners(SpriteBatch b, Rectangle area, int length, int thickness, Color color, int offset)
    {
        int left = area.Left + offset;
        int top = area.Top + offset;
        int right = area.Right + offset;
        int bottom = area.Bottom + offset;

        // top-left
        b.Draw(Game1.staminaRect, new Rectangle(left, top, length, thickness), color);
        b.Draw(Game1.staminaRect, new Rectangle(left, top, thickness, length), color);

        // top-right
        b.Draw(Game1.staminaRect, new Rectangle(right - length, top, length, thickness), color);
        b.Draw(Game1.staminaRect, new Rectangle(right - thickness, top, thickness, length), color);

        // bottom-left
        b.Draw(Game1.staminaRect, new Rectangle(left, bottom - thickness, length, thickness), color);
        b.Draw(Game1.staminaRect, new Rectangle(left, bottom - length, thickness, length), color);

        // bottom-right
        b.Draw(Game1.staminaRect, new Rectangle(right - length, bottom - thickness, length, thickness), color);
        b.Draw(Game1.staminaRect, new Rectangle(right - thickness, bottom - length, thickness, length), color);
    }


    /*********
    ** Private types
    *********/
    /// <summary>The viewfinder state for one player.</summary>
    private class State
    {
        public bool Active;

        /// <summary>Whether the viewfinder hid the HUD (so it should show it again on exit).</summary>
        public bool HidHud;

        /// <summary>The character close enough to ask to pose.</summary>
        public NPC? Nearby;

        /// <summary>The character posing for a photo now, if any.</summary>
        public NPC? PoseSubject;
        public string PoseCaption = "";
        public int PoseTicksLeft = -1;
    }
}
