using System.Collections.Concurrent;
using SkiaSharp;

namespace SharpMUTerm.Avalonia;

/// <summary>A transmitted picture and the cell box it was sent to span.</summary>
internal sealed record Picture(SKImage Image, int Columns, int Rows);

/// <summary>
/// The pictures the app has transmitted, by Kitty image id. This is the half of the Kitty protocol a terminal
/// keeps for itself — the pixels — and the placeholder cells in the grid are the other half: each names an id
/// in its foreground and a row and column in its combining marks, and the renderer looks the id up here.
/// <para>
/// Ids come from two numberings that never meet (the framework's own counts from 1, the client's pane
/// pictures from <c>0xC00000</c>), so one map serves both.
/// </para>
/// </summary>
internal sealed class PictureStore : IDisposable
{
    private readonly ConcurrentDictionary<uint, Picture> _pictures = new();

    public int Count => _pictures.Count;

    public bool TryGet(uint id, out Picture picture) => _pictures.TryGetValue(id, out picture!);

    /// <summary>Stores an encoded picture (PNG, as the framework's own image control sends). False if it does not decode.</summary>
    public bool AddEncoded(uint id, byte[] data, int columns, int rows)
    {
        var image = SKImage.FromEncodedData(data);
        if (image is null)
        {
            return false;
        }

        Replace(id, new Picture(image, Math.Max(1, columns), Math.Max(1, rows)));
        return true;
    }

    /// <summary>Stores raw RGB24 pixels, row-major — what the client sends for a picture in a pane.</summary>
    public void AddRgb(uint id, byte[] rgb, int pixelWidth, int pixelHeight, int columns, int rows)
    {
        Replace(id, new Picture(FromRgb(rgb, pixelWidth, pixelHeight), Math.Max(1, columns), Math.Max(1, rows)));
    }

    /// <summary>New pixels for a picture already placed, keeping its cell box.</summary>
    public void UpdateRgb(uint id, byte[] rgb, int pixelWidth, int pixelHeight)
    {
        if (_pictures.TryGetValue(id, out var existing))
        {
            Replace(id, existing with { Image = FromRgb(rgb, pixelWidth, pixelHeight) });
        }
    }

    public void Remove(uint id)
    {
        if (_pictures.TryRemove(id, out var gone))
        {
            gone.Image.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var id in _pictures.Keys)
        {
            Remove(id);
        }
    }

    private void Replace(uint id, Picture picture)
    {
        // Disposed only once out of the map: a frame being drawn may still hold the old one, and Skia
        // tolerates drawing an image whose managed wrapper is gone far worse than it tolerates a leak of one
        // frame. So the old image is left to the GC rather than disposed here.
        _pictures[id] = picture;
    }

    private static SKImage FromRgb(byte[] rgb, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgb.Length < width * height * 3)
        {
            throw new ArgumentException("Fewer bytes than width × height × 3.", nameof(rgb));
        }

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var rgba = new byte[width * height * 4];
        for (int i = 0, o = 0; o < rgba.Length; i += 3, o += 4)
        {
            rgba[o] = rgb[i];
            rgba[o + 1] = rgb[i + 1];
            rgba[o + 2] = rgb[i + 2];
            rgba[o + 3] = 255;
        }

        return SKImage.FromPixelCopy(info, rgba) ?? throw new InvalidOperationException("Skia refused the pixels.");
    }
}
