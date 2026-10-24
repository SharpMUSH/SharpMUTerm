using SharpConsoleUI.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace SharpMUTerm.Tui;

/// <summary>
/// Image bytes to a framework <see cref="PixelBuffer"/>, for both inline MXP pictures and the web view.
/// <para>
/// The framework's own decoder goes first. Since 2.7 that is StbImageSharp, which reads PNG, JPEG, BMP,
/// GIF and TGA and nothing else — so a WebP, which is what a modern web asset pipeline serves, threw
/// <see cref="UnsupportedImageFormatException"/> and the picture silently stayed a label. ImageSharp
/// takes whatever the framework refuses. It was on the decode path through 2.5.x as the framework's
/// own dependency, so this restores the format coverage the client had.
/// </para>
/// <para>
/// The fallback reads <b>WebP and nothing else</b>, with metadata skipped. That is the format the
/// framework lost, and it is also the whole of this client's exposure to ImageSharp 3.1's known
/// advisories: every one open against 3.1.12 is in the TIFF codecs or in ICC/EXIF parsing, and a
/// configuration holding only the WebP module, told to skip metadata, reaches neither. 4.x fixes them
/// but will not build without a Six Labors license key. Adding a format here means re-reading the
/// advisory list first.
/// </para>
/// </summary>
internal static class ImageDecoder
{
    /// <summary>
    /// Refuse a fallback image past this many pixels. The bytes are already capped at the fetch, but a
    /// compressed format can declare dimensions far beyond what its bytes suggest, and the decode
    /// allocates for the declared size.
    /// </summary>
    public const long MaxPixels = 24L * 1024 * 1024;

    private static readonly Configuration WebPOnly = new(new WebpConfigurationModule());

    /// <summary>Decodes <paramref name="bytes"/>; throws on a corrupt or unsupported payload.</summary>
    public static PixelBuffer Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return PixelBuffer.FromStream(stream);
        }
        catch (UnsupportedImageFormatException)
        {
            return DecodeWithImageSharp(bytes);
        }
    }

    private static PixelBuffer DecodeWithImageSharp(byte[] bytes)
    {
        var options = new DecoderOptions
        {
            Configuration = WebPOnly,
            MaxFrames = 1,
            SkipMetadata = true,
        };
        var info = Image.Identify(options, bytes);
        if ((long)info.Width * info.Height > MaxPixels)
        {
            throw new InvalidImageContentException($"{info.Width}x{info.Height} is past the decode ceiling.");
        }

        using var image = Image.Load<Rgba32>(options, bytes);
        var rgba = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rgba);
        return PixelBuffer.FromRgba32Bytes(rgba, image.Width, image.Height);
    }
}
