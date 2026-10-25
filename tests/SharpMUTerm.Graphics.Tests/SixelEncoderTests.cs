using SharpMUTerm.Graphics;

namespace SharpMUTerm.Graphics.Tests;

public class SixelEncoderTests
{
    private const string Esc = "\u001b";
    private const string Intro = Esc + "Pq";
    private const string Terminator = Esc + "\\";

    private readonly SixelEncoder _encoder = new();

    [Test]
    public async Task Encode_1x1White_GoldenOutput()
    {
        var image = new MemoryImageSource(1, 1, new[] { new Rgba32(255, 255, 255) });
        var result = _encoder.Encode(image);

        // White quantises to (100,100,100); single band, single sixel char '@' (bit0 set).
        var expected = Intro + "#0;2;100;100;100" + "#0@" + Terminator;
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Encode_2x2White_GoldenOutput()
    {
        var white = new Rgba32(255, 255, 255);
        var image = new MemoryImageSource(2, 2, new[] { white, white, white, white });
        var result = _encoder.Encode(image);

        // Two rows in one band -> bits 0 and 1 set -> value 3 -> char 'B' (63+3=66), per column.
        var expected = Intro + "#0;2;100;100;100" + "#0BB" + Terminator;
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Encode_TwoColors_UsesCarriageReturnOverlay()
    {
        // 2x1: black then white.
        var image = new MemoryImageSource(2, 1, new[]
        {
            new Rgba32(0, 0, 0),
            new Rgba32(255, 255, 255),
        });
        var result = _encoder.Encode(image);

        var expected = Intro
            + "#0;2;0;0;0#1;2;100;100;100" // palette: black=0, white=1
            + "#0@?"                        // register 0 set at col 0
            + "$"                           // graphics carriage return
            + "#1?@"                        // register 1 set at col 1
            + Terminator;
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Encode_StartsAndEndsWithSixelFraming()
    {
        var image = new MemoryImageSource(1, 1, new[] { new Rgba32(10, 20, 30) });
        var result = _encoder.Encode(image);

        await Assert.That(result.StartsWith(Esc + "P", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.EndsWith(Terminator, StringComparison.Ordinal)).IsTrue();
        await Assert.That(result).Contains("#0;2;"); // at least one palette registration
    }

    [Test]
    public async Task Encode_MultipleBands_EmitsGraphicsNewline()
    {
        // 1x7 image spans two six-row bands, so a '-' separator must appear.
        var pixels = new Rgba32[7];
        Array.Fill(pixels, new Rgba32(255, 255, 255));
        var image = new MemoryImageSource(1, 7, pixels);

        var result = _encoder.Encode(image);
        await Assert.That(result).Contains("-");
    }

    // ---- EncodeCompact ---------------------------------------------------------------------------

    [Test]
    public async Task EncodeCompact_UniformRow_IsOneRun()
    {
        var red = new Rgba32(255, 0, 0);
        var image = new MemoryImageSource(10, 6, Enumerable.Repeat(red, 60).ToArray());

        var result = SixelEncoder.EncodeCompact(image);

        await Assert.That(result).IsEqualTo(Esc + "P0;1;0q\"1;1;10;6#0;2;100;0;0#0!10~" + Terminator);
    }

    [Test]
    public async Task EncodeCompact_KeepsTheImagesOwnColours()
    {
        // Two colours the 6-level cube would merge into one.
        var a = new Rgba32(10, 10, 10);
        var b = new Rgba32(30, 30, 30);
        var image = new MemoryImageSource(2, 1, new[] { a, b });

        var result = SixelEncoder.EncodeCompact(image);

        await Assert.That(result).Contains("#0;2;").And.Contains("#1;2;");
    }

    [Test]
    public async Task EncodeCompact_HoldsToTwoHundredAndFiftySixRegisters()
    {
        var pixels = new Rgba32[64 * 64];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32((byte)(i * 8), (byte)(i / 32 * 8), (byte)(i / 1024 * 64));
        }

        var result = SixelEncoder.EncodeCompact(new MemoryImageSource(64, 64, pixels));

        await Assert.That(result).Contains("#255;2;");
        await Assert.That(result).DoesNotContain("#256");
    }
}
