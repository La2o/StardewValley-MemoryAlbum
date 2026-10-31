using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace MemoryAlbum.Framework;

/// <summary>The album: pages of polaroids, with filters for milestones, favorites, years and people.</summary>
public class AlbumMenu : IClickableMenu
{
    /*********
    ** Fields
    *********/
    private const int Columns = 3;
    private const int Rows = 2;
    private const int PageSize = Columns * Rows;
    private const int Padding = 36;

    private const int SlotIdBase = 9100;
    private const int PrevPageId = 9200;
    private const int NextPageId = 9201;
    private const int PrevFilterId = 9202;
    private const int NextFilterId = 9203;
    private const int FolderButtonId = 9204;

    private readonly AlbumStore Store;
    private readonly PhotoCamera Camera;
    private readonly ITranslationHelper Translation;
    private readonly ModConfig Config;
    private readonly Farmer Player;

    /// <summary>Every photo taken by the player, oldest first.</summary>
    private List<Photo> AllPhotos = new();

    /// <summary>The available filters.</summary>
    private List<PhotoFilter> Filters = new();

    /// <summary>The photos matching the current filter.</summary>
    private List<Photo> Filtered = new();

    private int FilterIndex;
    private int Page;

    private readonly List<ClickableComponent> Slots = new();
    private ClickableTextureComponent PrevPageButton = null!;
    private ClickableTextureComponent NextPageButton = null!;
    private ClickableTextureComponent PrevFilterButton = null!;
    private ClickableTextureComponent NextFilterButton = null!;
    private ClickableComponent FolderButton = null!;

    /// <summary>The area the filter label is drawn in.</summary>
    private Rectangle FilterLabelArea;

    /// <summary>The area the page number is drawn in.</summary>
    private Rectangle PageLabelArea;

    /// <summary>The photo under the cursor, if any.</summary>
    private Photo? HoveredPhoto;


    /*********
    ** Accessors
    *********/
    private int PageCount => Math.Max(1, (int)Math.Ceiling(this.Filtered.Count / (float)PageSize));


    /*********
    ** Public methods
    *********/
    public AlbumMenu(AlbumStore store, PhotoCamera camera, ITranslationHelper translation, ModConfig config, Farmer player)
    {
        this.Store = store;
        this.Camera = camera;
        this.Translation = translation;
        this.Config = config;
        this.Player = player;

        this.Refresh(goToLastPage: true);
        this.UpdateLayout();
        Game1.playSound("bigSelect");

        if (Game1.options.SnappyMenus)
            this.snapToDefaultClickableComponent();
    }

    /// <summary>Reload the photos from the store, e.g. after one was edited or deleted.</summary>
    /// <param name="goToLastPage">Whether to show the newest photos.</param>
    public void Refresh(bool goToLastPage = false)
    {
        string? filterId = this.Filters.Count > 0 ? this.Filters[this.FilterIndex].Id : null;

        this.AllPhotos = this.Store.GetPhotos(this.Player);
        this.Filters = this.BuildFilters();
        this.FilterIndex = Math.Max(0, this.Filters.FindIndex(p => p.Id == filterId));
        this.ApplyFilter(resetPage: false);

        if (goToLastPage)
            this.Page = this.PageCount - 1;
        this.Page = Math.Clamp(this.Page, 0, this.PageCount - 1);
        this.ReleaseOffPageTextures();
    }

    /// <summary>Show the page containing a photo.</summary>
    public void GoToPhoto(Photo photo)
    {
        int index = this.Filtered.IndexOf(photo);
        if (index >= 0)
            this.Page = index / PageSize;
        this.ReleaseOffPageTextures();
    }

    public override void snapToDefaultClickableComponent()
    {
        this.currentlySnappedComponent = this.getComponentWithID(this.GetPagePhotos().Any() ? SlotIdBase : NextFilterId);
        this.snapCursorToCurrentSnappedComponent();
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.UpdateLayout();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);

        if (this.PrevPageButton.containsPoint(x, y))
            this.ChangePage(-1);
        else if (this.NextPageButton.containsPoint(x, y))
            this.ChangePage(1);
        else if (this.PrevFilterButton.containsPoint(x, y))
            this.ChangeFilter(-1);
        else if (this.NextFilterButton.containsPoint(x, y))
            this.ChangeFilter(1);
        else if (this.FolderButton.containsPoint(x, y))
        {
            Game1.playSound("smallSelect");
            this.Store.OpenFolder();
        }
        else
        {
            Photo[] photos = this.GetPagePhotos();
            for (int i = 0; i < photos.Length; i++)
            {
                if (this.Slots[i].containsPoint(x, y))
                {
                    this.OpenViewer(photos[i]);
                    return;
                }
            }
        }
    }

    public override void receiveScrollWheelAction(int direction)
    {
        base.receiveScrollWheelAction(direction);
        this.ChangePage(direction > 0 ? -1 : 1);
    }

    public override void receiveKeyPress(Keys key)
    {
        switch (key)
        {
            case Keys.Left when !Game1.options.SnappyMenus:
                this.ChangePage(-1);
                return;

            case Keys.Right when !Game1.options.SnappyMenus:
                this.ChangePage(1);
                return;
        }

        base.receiveKeyPress(key);
    }

    public override void receiveGamePadButton(Buttons button)
    {
        base.receiveGamePadButton(button);

        switch (button)
        {
            case Buttons.LeftShoulder:
                this.ChangePage(-1);
                break;
            case Buttons.RightShoulder:
                this.ChangePage(1);
                break;
            case Buttons.LeftTrigger:
                this.ChangeFilter(-1);
                break;
            case Buttons.RightTrigger:
                this.ChangeFilter(1);
                break;
        }
    }

    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);

        this.PrevPageButton.tryHover(x, y);
        this.NextPageButton.tryHover(x, y);
        this.PrevFilterButton.tryHover(x, y);
        this.NextFilterButton.tryHover(x, y);

        this.HoveredPhoto = null;
        Photo[] photos = this.GetPagePhotos();
        for (int i = 0; i < photos.Length; i++)
        {
            if (this.Slots[i].containsPoint(x, y))
            {
                this.HoveredPhoto = photos[i];
                break;
            }
        }
    }

    protected override void cleanupBeforeExit()
    {
        base.cleanupBeforeExit();
        this.Store.ReleaseTextures();
    }

    public override void draw(SpriteBatch b)
    {
        if (!Game1.options.showMenuBackground)
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.6f);
        else
            this.drawBackground(b);

        drawTextureBox(b, Game1.menuTexture, AlbumDrawing.MenuFrame, this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);

        string title = this.Translation.Get("album.title", new { farm = Game1.MasterPlayer.farmName.Value });
        SpriteText.drawStringWithScrollCenteredAt(b, title, this.xPositionOnScreen + this.width / 2, this.yPositionOnScreen - 64);

        // filter bar
        PhotoFilter filter = this.Filters[this.FilterIndex];
        string filterLabel = this.Translation.Get("album.filter", new { filter = filter.Label, count = this.Filtered.Count });
        Vector2 filterSize = Game1.smallFont.MeasureString(filterLabel);
        Utility.drawTextWithShadow(b, filterLabel, Game1.smallFont, new Vector2(this.FilterLabelArea.Center.X - filterSize.X / 2, this.FilterLabelArea.Center.Y - filterSize.Y / 2), Game1.textColor);
        this.PrevFilterButton.draw(b);
        this.NextFilterButton.draw(b);
        AlbumDrawing.DrawButton(b, this.FolderButton.bounds, this.Translation.Get("album.open-folder"), this.FolderButton.containsPoint(Game1.getMouseX(), Game1.getMouseY()));

        // photos
        Photo[] photos = this.GetPagePhotos();
        if (photos.Length == 0)
        {
            string empty = this.AllPhotos.Count == 0
                ? this.Translation.Get("album.empty", new { key = this.Config.CameraKey.ToString() })
                : this.Translation.Get("album.empty-filter");
            string wrapped = Game1.parseText(empty, Game1.dialogueFont, this.width - Padding * 4);
            Vector2 size = Game1.dialogueFont.MeasureString(wrapped);
            Utility.drawTextWithShadow(b, wrapped, Game1.dialogueFont, new Vector2(this.xPositionOnScreen + (this.width - size.X) / 2, this.yPositionOnScreen + (this.height - size.Y) / 2), Game1.textColor * 0.75f);
        }
        for (int i = 0; i < photos.Length; i++)
            this.DrawPolaroid(b, photos[i], this.Slots[i].bounds, photos[i] == this.HoveredPhoto);

        // page navigation
        if (this.Page > 0)
            this.PrevPageButton.draw(b);
        if (this.Page < this.PageCount - 1)
            this.NextPageButton.draw(b);
        string pageLabel = this.Translation.Get("album.page", new { page = this.Page + 1, pages = this.PageCount });
        Vector2 pageSize = Game1.smallFont.MeasureString(pageLabel);
        Utility.drawTextWithShadow(b, pageLabel, Game1.smallFont, new Vector2(this.PageLabelArea.Center.X - pageSize.X / 2, this.PageLabelArea.Center.Y - pageSize.Y / 2), Game1.textColor);

        base.draw(b);

        if (this.HoveredPhoto is not null)
        {
            string details = PhotoText.GetDetails(this.HoveredPhoto, this.Translation);
            drawHoverText(b, Game1.parseText(details, Game1.smallFont, 420), Game1.smallFont, boldTitleText: PhotoText.GetCaptionOrPlaceholder(this.HoveredPhoto, this.Translation));
        }

        this.drawMouse(b);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Calculate the menu size and component positions for the current screen size.</summary>
    private void UpdateLayout()
    {
        const int titleSpace = 80; // room for the scroll title above the box
        const int barHeight = 56;

        this.width = Math.Min(1280, Game1.uiViewport.Width - 32);
        this.height = Math.Min(820, Game1.uiViewport.Height - titleSpace - 16);
        this.xPositionOnScreen = (Game1.uiViewport.Width - this.width) / 2;
        this.yPositionOnScreen = (Game1.uiViewport.Height - this.height + titleSpace) / 2;

        this.initializeUpperRightCloseButton();
        this.upperRightCloseButton.myID = upperRightCloseButton_ID;

        Rectangle inner = new(this.xPositionOnScreen + Padding, this.yPositionOnScreen + Padding, this.width - Padding * 2, this.height - Padding * 2);

        // filter bar
        int folderWidth = Math.Min(260, (int)Game1.smallFont.MeasureString(this.Translation.Get("album.open-folder")).X + 48);
        this.FolderButton = new ClickableComponent(new Rectangle(inner.Right - folderWidth, inner.Y, folderWidth, barHeight - 8), "folder") { myID = FolderButtonId };
        int filterWidth = Math.Min(560, inner.Width - folderWidth * 2 - 32);
        int filterX = inner.Center.X - filterWidth / 2;
        this.PrevFilterButton = new ClickableTextureComponent(new Rectangle(filterX, inner.Y + 4, 48, 44), Game1.mouseCursors, AlbumDrawing.LeftArrow, 4f) { myID = PrevFilterId };
        this.NextFilterButton = new ClickableTextureComponent(new Rectangle(filterX + filterWidth - 48, inner.Y + 4, 48, 44), Game1.mouseCursors, AlbumDrawing.RightArrow, 4f) { myID = NextFilterId };
        this.FilterLabelArea = new Rectangle(filterX + 56, inner.Y, filterWidth - 112, barHeight - 8);

        // page bar
        int pageY = inner.Bottom - barHeight + 8;
        this.PrevPageButton = new ClickableTextureComponent(new Rectangle(inner.Center.X - 160, pageY, 48, 44), Game1.mouseCursors, AlbumDrawing.LeftArrow, 4f) { myID = PrevPageId };
        this.NextPageButton = new ClickableTextureComponent(new Rectangle(inner.Center.X + 112, pageY, 48, 44), Game1.mouseCursors, AlbumDrawing.RightArrow, 4f) { myID = NextPageId };
        this.PageLabelArea = new Rectangle(inner.Center.X - 104, pageY, 208, 44);

        // photo grid
        int gridTop = inner.Y + barHeight;
        int gridHeight = pageY - 8 - gridTop;
        int cellWidth = inner.Width / Columns;
        int cellHeight = gridHeight / Rows;
        this.Slots.Clear();
        for (int i = 0; i < PageSize; i++)
        {
            int col = i % Columns;
            int row = i / Columns;
            Rectangle cell = new(inner.X + col * cellWidth, gridTop + row * cellHeight, cellWidth, cellHeight);
            cell.Inflate(-12, -10);

            this.Slots.Add(new ClickableComponent(cell, $"photo{i}")
            {
                myID = SlotIdBase + i,
                leftNeighborID = col > 0 ? SlotIdBase + i - 1 : PrevPageId,
                rightNeighborID = col < Columns - 1 ? SlotIdBase + i + 1 : NextPageId,
                upNeighborID = row > 0 ? SlotIdBase + i - Columns : (col == Columns - 1 ? FolderButtonId : NextFilterId),
                downNeighborID = row < Rows - 1 ? SlotIdBase + i + Columns : (col == 0 ? PrevPageId : NextPageId)
            });
        }

        // controller navigation for the bars
        this.PrevFilterButton.rightNeighborID = NextFilterId;
        this.PrevFilterButton.downNeighborID = SlotIdBase;
        this.NextFilterButton.leftNeighborID = PrevFilterId;
        this.NextFilterButton.rightNeighborID = FolderButtonId;
        this.NextFilterButton.downNeighborID = SlotIdBase + 1;
        this.FolderButton.leftNeighborID = NextFilterId;
        this.FolderButton.rightNeighborID = upperRightCloseButton_ID;
        this.FolderButton.downNeighborID = SlotIdBase + Columns - 1;
        this.upperRightCloseButton.leftNeighborID = FolderButtonId;
        this.upperRightCloseButton.downNeighborID = SlotIdBase + Columns - 1;
        this.PrevPageButton.rightNeighborID = NextPageId;
        this.PrevPageButton.upNeighborID = SlotIdBase + Columns;
        this.NextPageButton.leftNeighborID = PrevPageId;
        this.NextPageButton.upNeighborID = SlotIdBase + PageSize - 1;

        // populateClickableComponentList only finds public fields, so add the components explicitly
        this.populateClickableComponentList();
        this.allClickableComponents.AddRange(this.Slots);
        this.allClickableComponents.AddRange(new ClickableComponent[] { this.PrevPageButton, this.NextPageButton, this.PrevFilterButton, this.NextFilterButton, this.FolderButton });
        if (!this.allClickableComponents.Contains(this.upperRightCloseButton))
            this.allClickableComponents.Add(this.upperRightCloseButton);

        if (Game1.options.SnappyMenus && this.currentlySnappedComponent is not null)
        {
            this.currentlySnappedComponent = this.getComponentWithID(this.currentlySnappedComponent.myID);
            this.snapCursorToCurrentSnappedComponent();
        }
    }

    /// <summary>Draw a photo as a polaroid in a grid cell.</summary>
    private void DrawPolaroid(SpriteBatch b, Photo photo, Rectangle cell, bool hovered)
    {
        const int border = 10;
        int captionHeight = (int)(Game1.smallFont.LineSpacing * 1.9f);

        // fit a 16:9 polaroid in the cell
        int photoWidth = cell.Width - border * 2;
        int photoHeight = photoWidth * 9 / 16;
        int maxPhotoHeight = cell.Height - border * 2 - captionHeight;
        if (photoHeight > maxPhotoHeight)
        {
            photoHeight = maxPhotoHeight;
            photoWidth = photoHeight * 16 / 9;
        }
        int frameWidth = photoWidth + border * 2;
        int frameHeight = photoHeight + border * 2 + captionHeight;
        Rectangle frame = new(cell.Center.X - frameWidth / 2, cell.Center.Y - frameHeight / 2 - (hovered ? 4 : 0), frameWidth, frameHeight);

        // paper and shadow
        b.Draw(Game1.staminaRect, new Rectangle(frame.X + 6, frame.Y + 6 + (hovered ? 4 : 0), frame.Width, frame.Height), Color.Black * 0.25f);
        b.Draw(Game1.staminaRect, frame, photo.Milestone is not null ? new Color(255, 236, 170) : AlbumDrawing.Paper);

        // photo
        Rectangle photoArea = new(frame.X + border, frame.Y + border, photoWidth, photoHeight);
        AlbumDrawing.DrawPhoto(b, this.Store.GetTexture(photo), photo, photoArea);

        // caption and date
        float textWidth = frame.Width - border * 2;
        string caption = AlbumDrawing.Truncate(PhotoText.GetCaptionOrPlaceholder(photo, this.Translation), Game1.smallFont, textWidth);
        string date = AlbumDrawing.Truncate(PhotoText.GetDate(photo, this.Translation), Game1.smallFont, textWidth / 0.8f);
        b.DrawString(Game1.smallFont, caption, new Vector2(photoArea.X, photoArea.Bottom + 4), Game1.textColor);
        b.DrawString(Game1.smallFont, date, new Vector2(photoArea.X, photoArea.Bottom + 4 + Game1.smallFont.LineSpacing * 0.95f), Game1.textColor * 0.6f, 0, Vector2.Zero, 0.8f, SpriteEffects.None, 1);

        // badges
        if (photo.Milestone is not null)
            b.Draw(Game1.mouseCursors, new Rectangle(photoArea.Right - 36, photoArea.Y + 4, 32, 32), AlbumDrawing.StarIcon, Color.White);
        if (photo.Favorite)
            b.Draw(Game1.mouseCursors, new Rectangle(photoArea.X + 4, photoArea.Y + 4, 28, 24), AlbumDrawing.HeartIcon, Color.White);
    }

    /// <summary>Open a photo in the full-size viewer.</summary>
    private void OpenViewer(Photo photo)
    {
        Game1.playSound("bigSelect");
        Game1.activeClickableMenu = new PhotoViewerMenu(this.Store, this.Camera, this.Translation, this, this.Filtered, this.Filtered.IndexOf(photo));
    }

    private void ChangePage(int delta)
    {
        int page = Math.Clamp(this.Page + delta, 0, this.PageCount - 1);
        if (page == this.Page)
            return;

        this.Page = page;
        this.HoveredPhoto = null;
        this.ReleaseOffPageTextures();
        Game1.playSound("shwip");
    }

    private void ChangeFilter(int delta)
    {
        this.FilterIndex = (this.FilterIndex + delta + this.Filters.Count) % this.Filters.Count;
        this.ApplyFilter(resetPage: true);
        Game1.playSound("shwip");
    }

    private void ApplyFilter(bool resetPage)
    {
        PhotoFilter filter = this.Filters[this.FilterIndex];
        this.Filtered = this.AllPhotos.Where(filter.Matches).ToList();
        if (resetPage)
        {
            this.Page = this.PageCount - 1;
            this.HoveredPhoto = null;
            this.ReleaseOffPageTextures();
        }
    }

    private Photo[] GetPagePhotos()
    {
        return this.Filtered.Skip(this.Page * PageSize).Take(PageSize).ToArray();
    }

    /// <summary>Unload textures for photos which aren't on the current page, to limit memory use.</summary>
    private void ReleaseOffPageTextures()
    {
        this.Store.ReleaseTexturesExcept(this.GetPagePhotos());
    }

    /// <summary>Get the filters available for the current photos.</summary>
    private List<PhotoFilter> BuildFilters()
    {
        List<PhotoFilter> filters = new()
        {
            new PhotoFilter("all", this.Translation.Get("filter.all"), _ => true),
            new PhotoFilter("milestones", this.Translation.Get("filter.milestones"), p => p.Milestone is not null),
            new PhotoFilter("favorites", this.Translation.Get("filter.favorites"), p => p.Favorite),
            new PhotoFilter("posed", this.Translation.Get("filter.posed"), p => p.PosedWith is not null)
        };

        // by year
        foreach (int year in this.AllPhotos.Select(p => p.Year).Distinct().OrderBy(p => p))
            filters.Add(new PhotoFilter($"year:{year}", this.Translation.Get("filter.year", new { year }), p => p.Year == year));

        // by person, most photographed first
        var people = this.AllPhotos
            .SelectMany(p => p.People)
            .GroupBy(name => name)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.Key);
        foreach (string name in people)
            filters.Add(new PhotoFilter($"person:{name}", this.Translation.Get("filter.person", new { name }), p => p.People.Contains(name)));

        return filters;
    }


    /*********
    ** Private types
    *********/
    /// <summary>A way to filter the album.</summary>
    /// <param name="Id">A stable ID, so the filter can be restored after refreshing.</param>
    /// <param name="Label">The display text.</param>
    /// <param name="Matches">Whether a photo matches the filter.</param>
    private record PhotoFilter(string Id, string Label, Func<Photo, bool> Matches);
}
