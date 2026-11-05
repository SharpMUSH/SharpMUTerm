using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SharpMUTerm.Core.Avatars;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// A character's or a world's picture (<see cref="AvatarPicture"/>) as a bitmap, made once per picture and drawn in a
/// circle by the sidebar and the header. The client hands the same instance to every model while the source is
/// unchanged, so the bitmap is kept against that instance and goes when it does.
/// </summary>
internal static class AvatarBitmaps
{
    private static readonly ConditionalWeakTable<AvatarPicture, Bitmap> Made = new();

    /// <summary>The bitmap for <paramref name="picture"/>.</summary>
    public static Bitmap For(AvatarPicture picture) => Made.GetValue(picture, Make);

    private static Bitmap Make(AvatarPicture picture)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(picture.Size, picture.Size), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var row = picture.Size * 4;
        for (var y = 0; y < picture.Size; y++)
        {
            Marshal.Copy(picture.Rgba, y * row, buffer.Address + (y * buffer.RowBytes), row);
        }

        return bitmap;
    }

    /// <summary>
    /// Draws <paramref name="picture"/> as a disc of <paramref name="radius"/> at <paramref name="centre"/>, faded when
    /// <paramref name="dim"/> (a character who is not connected), with a hairline <paramref name="ring"/> round it if given.
    /// </summary>
    public static void Draw(DrawingContext context, AvatarPicture picture, Point centre, double radius, bool dim, IPen? ring = null)
    {
        var area = new Rect(centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
        using (context.PushGeometryClip(new EllipseGeometry(area)))
        using (context.PushOpacity(dim ? 0.45 : 1))
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
        {
            var bitmap = For(picture);
            context.DrawImage(bitmap, new Rect(bitmap.Size), area);
        }

        if (ring is not null)
        {
            context.DrawEllipse(null, ring, centre, radius, radius);
        }
    }
}
