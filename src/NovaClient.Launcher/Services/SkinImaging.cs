using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaClient.Launcher.Services;

/// <summary>
/// Renders the player's head and a 2D front-facing skin preview from the raw skin texture.
/// Supports modern 64×64 and legacy 64×32 skins. Pixel-art scaling (nearest neighbour).
/// </summary>
public static class SkinImaging
{
    public static BitmapSource? LoadSkin(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }

    /// <summary>8×8 face + hat overlay, scaled up crisply. Display at exactly 8×scale pixels.</summary>
    public static BitmapSource? RenderHead(BitmapSource skin, int scale = 8)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawPart(dc, skin, srcX: 8, srcY: 8, w: 8, h: 8, destX: 0, destY: 0, scale);   // face
            DrawPart(dc, skin, srcX: 40, srcY: 8, w: 8, h: 8, destX: 0, destY: 0, scale);  // hat layer
        }
        return Rasterize(visual, 8 * scale, 8 * scale);
    }

    /// <summary>Front-facing paper-doll: head, body, arms, legs (16×32 skin pixels).</summary>
    public static BitmapSource? RenderBodyFront(BitmapSource skin, int scale = 6)
    {
        var legacy = skin.PixelHeight < 64;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawPart(dc, skin, 8, 8, 8, 8, 4, 0, scale);      // head
            DrawPart(dc, skin, 40, 8, 8, 8, 4, 0, scale);     // hat
            DrawPart(dc, skin, 20, 20, 8, 12, 4, 8, scale);   // body
            DrawPart(dc, skin, 44, 20, 4, 12, 0, 8, scale);   // right arm
            if (legacy) DrawPart(dc, skin, 44, 20, 4, 12, 12, 8, scale, mirror: true);
            else DrawPart(dc, skin, 36, 52, 4, 12, 12, 8, scale); // left arm (64×64)
            DrawPart(dc, skin, 4, 20, 4, 12, 4, 20, scale);   // right leg
            if (legacy) DrawPart(dc, skin, 4, 20, 4, 12, 8, 20, scale, mirror: true);
            else DrawPart(dc, skin, 20, 52, 4, 12, 8, 20, scale); // left leg (64×64)
        }
        return Rasterize(visual, 16 * scale, 32 * scale);
    }

    private static void DrawPart(DrawingContext dc, BitmapSource skin, int srcX, int srcY, int w, int h,
        int destX, int destY, int scale, bool mirror = false)
    {
        if (srcX + w > skin.PixelWidth || srcY + h > skin.PixelHeight) return;
        var crop = new CroppedBitmap(skin, new Int32Rect(srcX, srcY, w, h));
        var rect = new Rect(destX * scale, destY * scale, w * scale, h * scale);
        if (mirror)
        {
            dc.PushTransform(new ScaleTransform(-1, 1, rect.X + rect.Width / 2, 0));
            dc.DrawImage(crop, rect);
            dc.Pop();
        }
        else
        {
            dc.DrawImage(crop, rect);
        }
    }

    private static BitmapSource Rasterize(DrawingVisual visual, int width, int height)
    {
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
