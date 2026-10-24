using SharpConsoleUI.Imaging;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// <see cref="ImageDecoder"/>: the framework's decoder first, ImageSharp for what it refuses.
/// </summary>
public class ImageDecoderTests
{
    [Test]
    public async Task TheFrameworkStillRefusesWebP()
    {
        // The reason the fallback exists. If a release starts reading WebP, this fails and the
        // fallback can be reconsidered.
        using var stream = new MemoryStream(TestImages.WebP(8, 8));
        await Assert.That(() => PixelBuffer.FromStream(stream)).Throws<UnsupportedImageFormatException>();
    }

    [Test]
    public async Task AWebPDecodesToTheSamePixelsAsThePngOfTheSameImage()
    {
        // Lossless, with partial alpha: the two paths must agree on channel order and on what alpha means.
        var fromPng = ImageDecoder.Decode(TestImages.RgbaPng(16, 12));
        var fromWebP = ImageDecoder.Decode(TestImages.WebP(16, 12));

        await Assert.That(fromWebP.Width).IsEqualTo(16);
        await Assert.That(fromWebP.Height).IsEqualTo(12);
        for (var y = 0; y < 12; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                await Assert.That(fromWebP.GetPixel(x, y)).IsEqualTo(fromPng.GetPixel(x, y));
            }
        }
    }

    [Test]
    public async Task BytesNoDecoderReadsStillThrow()
    {
        await Assert.That(() => ImageDecoder.Decode([1, 2, 3, 4, 5, 6, 7, 8])).ThrowsException();
    }

    [Test]
    public async Task TheFallbackReadsNoFormatButWebP()
    {
        // ImageSharp 3.1's open advisories are in its TIFF codecs; the fallback must not reach them.
        await Assert.That(() => ImageDecoder.Decode(TestImages.Tiff(8, 8))).ThrowsException();
    }

    [Test]
    public async Task TheWebViewDecodesAWebP()
    {
        var buffer = WebImageLoader.Decode(TestImages.WebP(32, 32), availableColumns: 200);

        await Assert.That(buffer).IsNotNull();
    }
}
