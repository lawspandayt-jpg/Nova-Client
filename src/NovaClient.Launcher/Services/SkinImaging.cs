using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaClient.Launcher.Services;

/// <summary>
/// Renders the player's head and 2D front-facing skin preview from the raw skin texture.
/// All compositing and scaling is done manually per-pixel (true nearest-neighbour) so WPF's
/// bitmap filtering can never blur the result. Output is baked at 2× display size, so even
/// fractional DPI scaling keeps hard pixel edges. Supports 64×64 and legacy 64×32 skins.
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

    /// <summary>8×8 face + hat overlay, baked at 128×128 (display at ≤64 px).</summary>
    public static BitmapSource? RenderHead(BitmapSource skin) => Render(skin, headOnly: true, factor: 16);

    /// <summary>Front paper-doll (16×32 skin pixels), baked at 192×384 (display at 96×192).</summary>
    public static BitmapSource? RenderBodyFront(BitmapSource skin) => Render(skin, headOnly: false, factor: 12);

    private static BitmapSource? Render(BitmapSource skin, bool headOnly, int factor)
    {
        try
        {
            var source = GetPixels(skin, out var srcW, out var srcH);
            var legacy = srcH < 64;

            int canvasW = headOnly ? 8 : 16, canvasH = headOnly ? 8 : 32;
            var canvas = new int[canvasW * canvasH];
            int headX = headOnly ? 0 : 4;

            Blit(source, srcW, 8, 8, 8, 8, canvas, canvasW, headX, 0);                    // head
            Blit(source, srcW, 40, 8, 8, 8, canvas, canvasW, headX, 0, overlay: true);    // hat
            if (!headOnly)
            {
                Blit(source, srcW, 20, 20, 8, 12, canvas, canvasW, 4, 8);                 // body
                Blit(source, srcW, 44, 20, 4, 12, canvas, canvasW, 0, 8);                 // right arm
                if (legacy) Blit(source, srcW, 44, 20, 4, 12, canvas, canvasW, 12, 8, mirror: true);
                else Blit(source, srcW, 36, 52, 4, 12, canvas, canvasW, 12, 8);           // left arm
                Blit(source, srcW, 4, 20, 4, 12, canvas, canvasW, 4, 20);                 // right leg
                if (legacy) Blit(source, srcW, 4, 20, 4, 12, canvas, canvasW, 8, 20, mirror: true);
                else Blit(source, srcW, 20, 52, 4, 12, canvas, canvasW, 8, 20);           // left leg
            }

            return Upscale(canvas, canvasW, canvasH, factor);
        }
        catch (Exception) { return null; }
    }

    private static int[] GetPixels(BitmapSource source, out int width, out int height)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        width = converted.PixelWidth;
        height = converted.PixelHeight;
        var pixels = new int[width * height];
        converted.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static void Blit(int[] src, int srcW, int sx, int sy, int w, int h,
        int[] dst, int dstW, int dx, int dy, bool mirror = false, bool overlay = false)
    {
        var srcH = src.Length / srcW;
        if (sx + w > srcW || sy + h > srcH) return;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var pixel = src[(sy + y) * srcW + sx + (mirror ? w - 1 - x : x)];
                var alpha = (pixel >>> 24) & 0xFF;
                if (overlay && alpha < 128) continue;      // hat/overlay: only solid pixels
                dst[(dy + y) * dstW + dx + x] = alpha < 128 && !overlay ? pixel | unchecked((int)0xFF000000) : pixel;
            }
        }
    }

    private static BitmapSource Upscale(int[] src, int w, int h, int factor)
    {
        var outW = w * factor;
        var outH = h * factor;
        var output = new int[outW * outH];
        for (var y = 0; y < outH; y++)
        {
            var srcRow = (y / factor) * w;
            var dstRow = y * outW;
            for (var x = 0; x < outW; x++)
                output[dstRow + x] = src[srcRow + x / factor];
        }
        var bitmap = new WriteableBitmap(outW, outH, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, outW, outH), output, outW * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }
}
