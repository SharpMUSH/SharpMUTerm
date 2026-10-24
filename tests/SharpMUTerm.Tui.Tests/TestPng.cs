using StbImageWriteSharp;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Encodes a real PNG for tests that need genuine image bytes. SharpConsoleUI decodes with
/// StbImageSharp since 2.7, so the encoder is its sibling rather than a second imaging library.
/// </summary>
internal static class TestPng
{
    public static byte[] Encode(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var data = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                var i = ((y * width) + x) * 3;
                data[i] = r;
                data[i + 1] = g;
                data[i + 2] = b;
            }
        }

        using var stream = new MemoryStream();
        new ImageWriter().WritePng(data, width, height, ColorComponents.RedGreenBlue, stream);
        return stream.ToArray();
    }
}
