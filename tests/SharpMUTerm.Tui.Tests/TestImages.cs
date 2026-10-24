using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Image bytes in the formats the framework's own decoder refuses, for the fallback in
/// <see cref="ImageDecoder"/>. <see cref="TestPng"/> stays the source of plain PNGs.
/// </summary>
internal static class TestImages
{
    /// <summary>A pattern with every channel varying and alpha that is neither 0 nor 255 in places.</summary>
    public static Rgba32 Pattern(int x, int y) =>
        new((byte)(x * 7), (byte)(y * 11), (byte)((x + y) * 5), (byte)(x % 4 == 0 ? 128 : 255));

    public static byte[] WebP(int width, int height) =>
        Encode(width, height, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });

    public static byte[] Tiff(int width, int height) => Encode(width, height, new TiffEncoder());

    public static byte[] RgbaPng(int width, int height) => Encode(width, height, new PngEncoder());

    private static byte[] Encode(int width, int height, SixLabors.ImageSharp.Formats.IImageEncoder encoder)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = Pattern(x, y);
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }
}
