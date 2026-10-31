using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace MemoryAlbum.Framework;

/// <summary>Drawing helpers shared by the album menus.</summary>
public static class AlbumDrawing
{
    /*********
    ** Accessors
    *********/
    /// <summary>The polaroid paper color.</summary>
    public static readonly Color Paper = new(250, 246, 236);

    /// <summary>The source rectangle for a small button frame in <see cref="Game1.mouseCursors"/>.</summary>
    public static readonly Rectangle ButtonFrame = new(432, 439, 9, 9);

    /// <summary>The source rectangle for the standard menu frame in <see cref="Game1.menuTexture"/>.</summary>
    public static readonly Rectangle MenuFrame = new(0, 256, 60, 60);

    /// <summary>The source rectangle for the gold star in <see cref="Game1.mouseCursors"/>, used for milestone photos.</summary>
    public static readonly Rectangle StarIcon = new(346, 400, 8, 8);

    /// <summary>The source rectangles for the filled and empty hearts in <see cref="Game1.mouseCursors"/>, used for favorites.</summary>
    public static readonly Rectangle HeartIcon = new(211, 428, 7, 6);
    public static readonly Rectangle EmptyHeartIcon = new(218, 428, 7, 6);

    /// <summary>The source rectangles for the left and right arrows in <see cref="Game1.mouseCursors"/>.</summary>
    public static readonly Rectangle LeftArrow = new(352, 495, 12, 11);
    public static readonly Rectangle RightArrow = new(365, 495, 12, 11);


    /*********
    ** Public methods
    *********/
    /// <summary>Get the largest rectangle with the given aspect ratio which fits centered inside an area.</summary>
    public static Rectangle Fit(Rectangle area, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return area;

        float scale = Math.Min(area.Width / (float)width, area.Height / (float)height);
        int w = (int)(width * scale);
        int h = (int)(height * scale);
        return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }

    /// <summary>Draw a photo inside an area, letterboxed to keep its aspect ratio.</summary>
    public static void DrawPhoto(SpriteBatch b, Texture2D? texture, Photo photo, Rectangle area)
    {
        b.Draw(Game1.staminaRect, area, new Color(40, 34, 30));
        if (texture is null)
        {
            // missing image
            string text = "?";
            Vector2 size = Game1.dialogueFont.MeasureString(text);
            b.DrawString(Game1.dialogueFont, text, new Vector2(area.Center.X - size.X / 2, area.Center.Y - size.Y / 2), Color.White * 0.5f);
            return;
        }

        b.Draw(texture, Fit(area, texture.Width, texture.Height), Color.White);
    }

    /// <summary>Draw a plain text button.</summary>
    public static void DrawButton(SpriteBatch b, Rectangle bounds, string label, bool hovered, bool enabled = true)
    {
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, ButtonFrame, bounds.X, bounds.Y, bounds.Width, bounds.Height, hovered && enabled ? Color.Wheat : Color.White, 4f, false);

        Vector2 size = Game1.smallFont.MeasureString(label);
        float scale = Math.Min(1f, (bounds.Width - 16) / Math.Max(1, size.X));
        Vector2 position = new(bounds.Center.X - size.X * scale / 2, bounds.Center.Y - size.Y * scale / 2 + 2);
        Utility.drawTextWithShadow(b, label, Game1.smallFont, position, Game1.textColor * (enabled ? 1f : 0.4f), scale);
    }

    /// <summary>Truncate text with an ellipsis so it fits a width.</summary>
    public static string Truncate(string text, SpriteFont font, float maxWidth)
    {
        if (font.MeasureString(text).X <= maxWidth)
            return text;

        const string ellipsis = "...";
        for (int length = text.Length - 1; length > 0; length--)
        {
            string candidate = text[..length].TrimEnd() + ellipsis;
            if (font.MeasureString(candidate).X <= maxWidth)
                return candidate;
        }
        return ellipsis;
    }
}
