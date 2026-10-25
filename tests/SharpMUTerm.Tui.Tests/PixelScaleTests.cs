using SharpConsoleUI.Imaging;

namespace SharpMUTerm.Tui.Tests;

public class PixelScaleTests
{
    private static PixelBuffer Gradient(int width, int height)
    {
        var buffer = new PixelBuffer(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                buffer.SetPixel(x, y, new ImagePixel((byte)(x * 255 / (width - 1)), (byte)(y * 255 / (height - 1)), 0));
            }
        }

        return buffer;
    }

    [Test]
    public async Task TheFrameworksResizeClampsToFiveHundred()
    {
        // Why PixelScale exists. When a release stops clamping, this fails and PixelScale can go.
        var resized = Gradient(400, 384).Resize(1140, 480);

        await Assert.That(resized.Width).IsEqualTo(500);
    }

    [Test]
    [Arguments(400, 384, 1140, 480)]
    [Arguments(2000, 1000, 912, 384)]
    [Arguments(10, 10, 3, 700)]
    public async Task AStretchIsExactlyTheSizeAskedFor(int fromWidth, int fromHeight, int width, int height)
    {
        var stretched = PixelScale.Stretch(Gradient(fromWidth, fromHeight), width, height);

        await Assert.That((stretched.Width, stretched.Height)).IsEqualTo((width, height));
    }

    [Test]
    public async Task TheCornersKeepTheirColoursAndTheMiddleIsBetween()
    {
        var stretched = PixelScale.Stretch(Gradient(400, 384), 1140, 480);

        await Assert.That(stretched.GetPixel(0, 0)).IsEqualTo(new ImagePixel(0, 0, 0));
        await Assert.That(stretched.GetPixel(1139, 479)).IsEqualTo(new ImagePixel(255, 255, 0));
        await Assert.That((int)stretched.GetPixel(570, 240).R).IsBetween(120, 135);
    }
}
