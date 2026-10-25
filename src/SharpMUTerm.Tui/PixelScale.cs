using SharpConsoleUI.Imaging;

namespace SharpMUTerm.Tui;

/// <summary>
/// Scales a picture to exactly the size asked for.
/// <para>
/// <see cref="PixelBuffer.Resize"/> is not that: it clamps each side to
/// <c>ImagingDefaults.MaxImageDimension</c> (500 at SharpConsoleUI 2.7.8) without saying so, so asking it
/// for 1140×480 returns 500×480 — a picture squeezed to 44% of its width, which is what the Sixel overlay
/// drew on Windows Terminal. Shrinking averages every source pixel under the target one; growing is
/// bilinear.
/// </para>
/// </summary>
internal static class PixelScale
{
    public static PixelBuffer Stretch(PixelBuffer source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width == source.Width && height == source.Height)
        {
            return source;
        }

        var result = new PixelBuffer(width, height);
        var sx = (double)source.Width / width;
        var sy = (double)source.Height / height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                result.SetPixel(x, y, sx > 1 || sy > 1 ? Average(source, x * sx, y * sy, sx, sy) : Bilinear(source, (x + 0.5) * sx - 0.5, (y + 0.5) * sy - 0.5));
            }
        }

        return result;
    }

    /// <summary>The mean of the source pixels whose top-left corners fall under one target pixel.</summary>
    private static ImagePixel Average(PixelBuffer source, double left, double top, double sx, double sy)
    {
        var x0 = Math.Min((int)left, source.Width - 1);
        var y0 = Math.Min((int)top, source.Height - 1);
        var x1 = Math.Clamp((int)Math.Ceiling(left + sx), x0 + 1, source.Width);
        var y1 = Math.Clamp((int)Math.Ceiling(top + sy), y0 + 1, source.Height);
        long r = 0, g = 0, b = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var p = source.GetPixel(x, y);
                r += p.R;
                g += p.G;
                b += p.B;
            }
        }

        var n = (x1 - x0) * (y1 - y0);
        return new ImagePixel((byte)((r + (n / 2)) / n), (byte)((g + (n / 2)) / n), (byte)((b + (n / 2)) / n));
    }

    private static ImagePixel Bilinear(PixelBuffer source, double fx, double fy)
    {
        fx = Math.Clamp(fx, 0, source.Width - 1);
        fy = Math.Clamp(fy, 0, source.Height - 1);
        var x0 = (int)fx;
        var y0 = (int)fy;
        var x1 = Math.Min(x0 + 1, source.Width - 1);
        var y1 = Math.Min(y0 + 1, source.Height - 1);
        var tx = fx - x0;
        var ty = fy - y0;
        var p00 = source.GetPixel(x0, y0);
        var p10 = source.GetPixel(x1, y0);
        var p01 = source.GetPixel(x0, y1);
        var p11 = source.GetPixel(x1, y1);

        byte Mix(byte a, byte b, byte c, byte d) =>
            (byte)Math.Round(((a * (1 - tx)) + (b * tx)) * (1 - ty) + (((c * (1 - tx)) + (d * tx)) * ty));

        return new ImagePixel(Mix(p00.R, p10.R, p01.R, p11.R), Mix(p00.G, p10.G, p01.G, p11.G), Mix(p00.B, p10.B, p01.B, p11.B));
    }
}
