using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace MemoryAlbum.Framework;

/// <summary>Shows one photo at full size, with its details, an editable caption, and actions.</summary>
public class PhotoViewerMenu : IClickableMenu
{
    /*********
    ** Fields
    *********/
    private const int Padding = 36;
    private const int CaptionLimit = 80;

    private const int PrevId = 9300;
    private const int NextId = 9301;
    private const int CaptionId = 9302;
    private const int FavoriteId = 9303;
    private const int ExportId = 9304;
    private const int DeleteId = 9305;
    private const int BackId = 9306;

    private readonly AlbumStore Store;
    private readonly PhotoCamera Camera;
    private readonly ITranslationHelper Translation;
    private readonly AlbumMenu Album;
    private readonly List<Photo> Photos;
    private int Index;

    private readonly TextBox CaptionBox;
    private ClickableComponent CaptionArea = null!;
    private ClickableTextureComponent PrevButton = null!;
    private ClickableTextureComponent NextButton = null!;
    private ClickableComponent FavoriteButton = null!;
    private ClickableComponent ExportButton = null!;
    private ClickableComponent DeleteButton = null!;
    private ClickableComponent BackButton = null!;

    /// <summary>The area containing the photo, its paper, and the caption.</summary>
    private Rectangle PhotoColumn;

    /// <summary>The area containing the photo details.</summary>
    private Rectangle DetailsArea;

    /// <summary>A short status message (like "Polaroid saved"), and how many ticks it stays visible.</summary>
    private string? Status;
    private int StatusTicks;


    /*********
    ** Accessors
    *********/
    private Photo Photo => this.Photos[this.Index];


    /*********
    ** Public methods
    *********/
    public PhotoViewerMenu(AlbumStore store, PhotoCamera camera, ITranslationHelper translation, AlbumMenu album, List<Photo> photos, int index)
    {
        this.Store = store;
        this.Camera = camera;
        this.Translation = translation;
        this.Album = album;
        this.Photos = photos;
        this.Index = Math.Clamp(index, 0, photos.Count - 1);

        this.CaptionBox = new TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"), null, Game1.smallFont, Game1.textColor)
        {
            textLimit = CaptionLimit,
            // the box is resized to fit each photo, so limiting text to its width would silently truncate captions
            limitWidth = false,
            TitleText = translation.Get("viewer.caption-title")
        };
        this.CaptionBox.OnEnterPressed += _ => this.CommitCaption();
        this.LoadCaption();

        this.UpdateLayout();
        if (Game1.options.SnappyMenus)
            this.snapToDefaultClickableComponent();
    }

    public override void snapToDefaultClickableComponent()
    {
        this.currentlySnappedComponent = this.getComponentWithID(this.Photos.Count > 1 ? NextId : BackId);
        this.snapCursorToCurrentSnappedComponent();
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.UpdateLayout();
    }

    public override bool readyToClose()
    {
        return !this.CaptionBox.Selected;
    }

    public override void update(GameTime time)
    {
        base.update(time);
        if (this.StatusTicks > 0 && --this.StatusTicks == 0)
            this.Status = null;
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        // caption box
        bool wasEditing = this.CaptionBox.Selected;
        this.CaptionBox.Update(); // selects or deselects based on the cursor position
        if (wasEditing && !this.CaptionBox.Selected)
            this.CommitCaption();
        if (this.CaptionBox.Selected)
            return;

        if (this.PrevButton.containsPoint(x, y))
            this.Move(-1);
        else if (this.NextButton.containsPoint(x, y))
            this.Move(1);
        else if (this.FavoriteButton.containsPoint(x, y))
            this.ToggleFavorite();
        else if (this.ExportButton.containsPoint(x, y))
            this.Export();
        else if (this.DeleteButton.containsPoint(x, y))
            this.ConfirmDelete();
        else if (this.BackButton.containsPoint(x, y))
            this.GoBack();
    }

    public override void receiveKeyPress(Keys key)
    {
        // typing a caption
        if (this.CaptionBox.Selected)
        {
            if (key == Keys.Escape)
            {
                this.CaptionBox.Selected = false;
                this.CommitCaption();
            }
            return;
        }

        if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
        {
            this.GoBack();
            return;
        }

        if (!Game1.options.SnappyMenus)
        {
            if (key == Keys.Left)
                this.Move(-1);
            else if (key == Keys.Right)
                this.Move(1);
        }

        base.receiveKeyPress(key);
    }

    public override void receiveGamePadButton(Buttons button)
    {
        base.receiveGamePadButton(button);

        switch (button)
        {
            case Buttons.LeftShoulder:
                this.Move(-1);
                break;
            case Buttons.RightShoulder:
                this.Move(1);
                break;
        }
    }

    public override void receiveScrollWheelAction(int direction)
    {
        base.receiveScrollWheelAction(direction);
        if (!this.CaptionBox.Selected)
            this.Move(direction > 0 ? -1 : 1);
    }

    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
        this.PrevButton.tryHover(x, y);
        this.NextButton.tryHover(x, y);
    }

    public override void draw(SpriteBatch b)
    {
        if (!Game1.options.showMenuBackground)
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.7f);
        else
            this.drawBackground(b);

        drawTextureBox(b, Game1.menuTexture, AlbumDrawing.MenuFrame, this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);

        Photo photo = this.Photo;
        SpriteText.drawStringWithScrollCenteredAt(b, PhotoText.GetDate(photo, this.Translation), this.xPositionOnScreen + this.width / 2, this.yPositionOnScreen - 64);

        // polaroid
        Texture2D? texture = this.Store.GetTexture(photo);
        const int border = 14;
        int captionSpace = this.CaptionBox.Height + border;
        Rectangle photoSpace = new(this.PhotoColumn.X + border, this.PhotoColumn.Y + border, this.PhotoColumn.Width - border * 2, this.PhotoColumn.Height - border * 2 - captionSpace);
        Rectangle photoArea = AlbumDrawing.Fit(photoSpace, texture?.Width ?? Math.Max(1, photo.Width), texture?.Height ?? Math.Max(1, photo.Height));
        Rectangle paper = new(photoArea.X - border, photoArea.Y - border, photoArea.Width + border * 2, photoArea.Height + border * 2 + captionSpace);
        b.Draw(Game1.staminaRect, new Rectangle(paper.X + 8, paper.Y + 8, paper.Width, paper.Height), Color.Black * 0.3f);
        b.Draw(Game1.staminaRect, paper, photo.Milestone is not null ? new Color(255, 236, 170) : AlbumDrawing.Paper);
        AlbumDrawing.DrawPhoto(b, texture, photo, photoArea);
        if (photo.Milestone is not null)
            b.Draw(Game1.mouseCursors, new Rectangle(photoArea.Right - 44, photoArea.Y + 8, 36, 36), AlbumDrawing.StarIcon, Color.White);

        // caption
        this.CaptionBox.X = photoArea.X;
        this.CaptionBox.Y = photoArea.Bottom + border / 2;
        this.CaptionBox.Width = photoArea.Width;
        this.CaptionArea.bounds = new Rectangle(this.CaptionBox.X, this.CaptionBox.Y, this.CaptionBox.Width, this.CaptionBox.Height);
        this.CaptionBox.Draw(b);
        if (!this.CaptionBox.Selected && string.IsNullOrWhiteSpace(this.CaptionBox.Text))
            b.DrawString(Game1.smallFont, this.Translation.Get("viewer.caption-placeholder"), new Vector2(this.CaptionBox.X + 16, this.CaptionBox.Y + 12), Game1.textColor * 0.45f);

        // arrows
        if (this.Index > 0)
            this.PrevButton.draw(b);
        if (this.Index < this.Photos.Count - 1)
            this.NextButton.draw(b);

        // details
        string details = PhotoText.GetDetails(photo, this.Translation);
        if (photo.Milestone is not null)
            details = this.Translation.Get("viewer.milestone") + "\n" + details;
        details += "\n\n" + this.Translation.Get("viewer.position", new { index = this.Index + 1, count = this.Photos.Count });
        string wrapped = Game1.parseText(details, Game1.smallFont, this.DetailsArea.Width);
        Utility.drawTextWithShadow(b, wrapped, Game1.smallFont, new Vector2(this.DetailsArea.X, this.DetailsArea.Y), Game1.textColor);

        if (this.Status is not null)
        {
            Vector2 detailsSize = Game1.smallFont.MeasureString(wrapped);
            string status = Game1.parseText(this.Status, Game1.smallFont, this.DetailsArea.Width);
            Utility.drawTextWithShadow(b, status, Game1.smallFont, new Vector2(this.DetailsArea.X, this.DetailsArea.Y + detailsSize.Y + 24), new Color(40, 120, 40));
        }

        // buttons
        int mouseX = Game1.getMouseX();
        int mouseY = Game1.getMouseY();
        string favoriteLabel = this.Translation.Get(photo.Favorite ? "viewer.unfavorite" : "viewer.favorite");
        AlbumDrawing.DrawButton(b, this.FavoriteButton.bounds, favoriteLabel, this.FavoriteButton.containsPoint(mouseX, mouseY));
        b.Draw(Game1.mouseCursors, new Rectangle(this.FavoriteButton.bounds.X + 14, this.FavoriteButton.bounds.Center.Y - 10, 24, 20), photo.Favorite ? AlbumDrawing.HeartIcon : AlbumDrawing.EmptyHeartIcon, Color.White);
        AlbumDrawing.DrawButton(b, this.ExportButton.bounds, this.Translation.Get("viewer.export"), this.ExportButton.containsPoint(mouseX, mouseY));
        AlbumDrawing.DrawButton(b, this.DeleteButton.bounds, this.Translation.Get("viewer.delete"), this.DeleteButton.containsPoint(mouseX, mouseY));
        AlbumDrawing.DrawButton(b, this.BackButton.bounds, this.Translation.Get("viewer.back"), this.BackButton.containsPoint(mouseX, mouseY));

        base.draw(b);
        this.drawMouse(b);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Calculate the menu size and component positions for the current screen size.</summary>
    private void UpdateLayout()
    {
        const int titleSpace = 80;
        const int arrowWidth = 48;
        const int buttonHeight = 56;
        const int buttonGap = 12;

        this.width = Math.Min(1440, Game1.uiViewport.Width - 32);
        this.height = Math.Min(880, Game1.uiViewport.Height - titleSpace - 16);
        this.xPositionOnScreen = (Game1.uiViewport.Width - this.width) / 2;
        this.yPositionOnScreen = (Game1.uiViewport.Height - this.height + titleSpace) / 2;

        Rectangle inner = new(this.xPositionOnScreen + Padding, this.yPositionOnScreen + Padding, this.width - Padding * 2, this.height - Padding * 2);

        int panelWidth = Math.Clamp(inner.Width * 28 / 100, 260, 400);
        Rectangle panel = new(inner.Right - panelWidth, inner.Y, panelWidth, inner.Height);
        this.PhotoColumn = new Rectangle(inner.X + arrowWidth + 12, inner.Y, panel.X - 24 - (inner.X + arrowWidth + 12) - arrowWidth - 12, inner.Height);

        int arrowY = this.PhotoColumn.Center.Y - 22;
        this.PrevButton = new ClickableTextureComponent(new Rectangle(inner.X, arrowY, arrowWidth, 44), Game1.mouseCursors, AlbumDrawing.LeftArrow, 4f) { myID = PrevId };
        this.NextButton = new ClickableTextureComponent(new Rectangle(this.PhotoColumn.Right + 12, arrowY, arrowWidth, 44), Game1.mouseCursors, AlbumDrawing.RightArrow, 4f) { myID = NextId };

        int buttonsTop = panel.Bottom - buttonHeight * 4 - buttonGap * 3;
        this.FavoriteButton = new ClickableComponent(new Rectangle(panel.X, buttonsTop, panel.Width, buttonHeight), "favorite") { myID = FavoriteId };
        this.ExportButton = new ClickableComponent(new Rectangle(panel.X, buttonsTop + (buttonHeight + buttonGap), panel.Width, buttonHeight), "export") { myID = ExportId };
        this.DeleteButton = new ClickableComponent(new Rectangle(panel.X, buttonsTop + (buttonHeight + buttonGap) * 2, panel.Width, buttonHeight), "delete") { myID = DeleteId };
        this.BackButton = new ClickableComponent(new Rectangle(panel.X, buttonsTop + (buttonHeight + buttonGap) * 3, panel.Width, buttonHeight), "back") { myID = BackId };
        this.DetailsArea = new Rectangle(panel.X, panel.Y, panel.Width, buttonsTop - panel.Y - 16);

        // the caption box is positioned when drawn, since it depends on the photo's size
        this.CaptionArea = new ClickableComponent(new Rectangle(this.PhotoColumn.X, this.PhotoColumn.Bottom - this.CaptionBox.Height, this.PhotoColumn.Width, this.CaptionBox.Height), "caption") { myID = CaptionId };

        // controller navigation
        this.PrevButton.rightNeighborID = NextId;
        this.PrevButton.downNeighborID = CaptionId;
        this.NextButton.leftNeighborID = PrevId;
        this.NextButton.rightNeighborID = FavoriteId;
        this.NextButton.downNeighborID = CaptionId;
        this.CaptionArea.upNeighborID = NextId;
        this.CaptionArea.rightNeighborID = FavoriteId;
        this.FavoriteButton.leftNeighborID = NextId;
        this.FavoriteButton.downNeighborID = ExportId;
        this.ExportButton.leftNeighborID = CaptionId;
        this.ExportButton.upNeighborID = FavoriteId;
        this.ExportButton.downNeighborID = DeleteId;
        this.DeleteButton.leftNeighborID = CaptionId;
        this.DeleteButton.upNeighborID = ExportId;
        this.DeleteButton.downNeighborID = BackId;
        this.BackButton.leftNeighborID = CaptionId;
        this.BackButton.upNeighborID = DeleteId;

        this.populateClickableComponentList();
        this.allClickableComponents.AddRange(new[] { this.PrevButton, this.NextButton, this.CaptionArea, this.FavoriteButton, this.ExportButton, this.DeleteButton, this.BackButton });

        if (Game1.options.SnappyMenus && this.currentlySnappedComponent is not null)
        {
            this.currentlySnappedComponent = this.getComponentWithID(this.currentlySnappedComponent.myID);
            this.snapCursorToCurrentSnappedComponent();
        }
    }

    /// <summary>Show another photo.</summary>
    private void Move(int delta)
    {
        int index = Math.Clamp(this.Index + delta, 0, this.Photos.Count - 1);
        if (index == this.Index)
            return;

        this.CommitCaption();
        this.Index = index;
        this.LoadCaption();
        this.Status = null;
        this.Store.ReleaseTexturesExcept(new[] { this.Photo });
        Game1.playSound("shwip");
    }

    private void LoadCaption()
    {
        this.CaptionBox.Text = this.Photo.Caption;
    }

    /// <summary>Save the caption box text to the photo, if it changed.</summary>
    private void CommitCaption()
    {
        string caption = this.CaptionBox.Text.Trim();
        if (caption == this.Photo.Caption)
            return;

        this.Photo.Caption = caption;
        this.Store.Save();
    }

    private void ToggleFavorite()
    {
        this.Photo.Favorite = !this.Photo.Favorite;
        this.Store.Save();
        Game1.playSound(this.Photo.Favorite ? "give_gift" : "smallSelect");
    }

    private void Export()
    {
        this.CommitCaption();

        string? path = this.Camera.ExportPolaroid(this.Photo);
        if (path is not null)
        {
            Game1.playSound("cameraNoise");
            this.ShowStatus(this.Translation.Get("viewer.exported"));
        }
        else
        {
            Game1.playSound("cancel");
            this.ShowStatus(this.Translation.Get("viewer.export-failed"));
        }
    }

    private void ConfirmDelete()
    {
        this.CommitCaption();
        Game1.playSound("smallSelect");

        Game1.activeClickableMenu = new ConfirmationDialog(
            this.Translation.Get("viewer.delete-confirm"),
            onConfirm: _ =>
            {
                Photo photo = this.Photo;
                this.Store.Delete(photo);
                this.Photos.Remove(photo);
                Game1.playSound("trashcan");

                if (this.Photos.Count == 0)
                {
                    this.GoBack();
                    return;
                }

                this.Index = Math.Clamp(this.Index, 0, this.Photos.Count - 1);
                this.LoadCaption();
                Game1.activeClickableMenu = this;
            },
            onCancel: _ => Game1.activeClickableMenu = this
        );
    }

    /// <summary>Return to the album page containing the current photo.</summary>
    private void GoBack()
    {
        this.CommitCaption();
        this.CaptionBox.Selected = false;

        Photo? current = this.Photos.Count > 0 ? this.Photo : null;
        this.Album.Refresh();
        if (current is not null)
            this.Album.GoToPhoto(current);

        Game1.activeClickableMenu = this.Album;
        Game1.playSound("bigDeSelect");
    }

    private void ShowStatus(string text)
    {
        this.Status = text;
        this.StatusTicks = 60 * 5;
    }
}
