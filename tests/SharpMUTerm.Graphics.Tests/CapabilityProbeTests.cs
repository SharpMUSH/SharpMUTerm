using SharpMUTerm.Graphics;

namespace SharpMUTerm.Graphics.Tests;

public class CapabilityProbeTests
{
    private static TerminalCapabilities Detect(params (string Key, string? Value)[] vars)
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in vars)
        {
            env[key] = value;
        }

        return CapabilityProbe.Detect(env);
    }

    [Test]
    public async Task EmptyEnvironment_DefaultsToNone()
    {
        var caps = Detect();
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.None);
        await Assert.That(caps.SupportsKittyGraphics).IsFalse();
        await Assert.That(caps.SupportsSixel).IsFalse();
        await Assert.That(caps.SupportsTrueColor).IsFalse();
    }

    [Test]
    public async Task TrueColorOnly_SelectsHalfBlock()
    {
        var caps = Detect(("COLORTERM", "truecolor"));
        await Assert.That(caps.SupportsTrueColor).IsTrue();
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.HalfBlock);
    }

    [Test]
    [Arguments("truecolor")]
    [Arguments("24bit")]
    public async Task ColorTerm_EnablesTrueColor(string value)
    {
        var caps = Detect(("COLORTERM", value));
        await Assert.That(caps.SupportsTrueColor).IsTrue();
    }

    [Test]
    public async Task Kitty_ViaTerm()
    {
        var caps = Detect(("TERM", "xterm-kitty"));
        await Assert.That(caps.SupportsKittyGraphics).IsTrue();
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Kitty);
    }

    [Test]
    public async Task Kitty_ViaWindowId()
    {
        var caps = Detect(("KITTY_WINDOW_ID", "3"));
        await Assert.That(caps.SupportsKittyGraphics).IsTrue();
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Kitty);
    }

    [Test]
    public async Task Kitty_ViaGhostty()
    {
        var caps = Detect(("TERM_PROGRAM", "ghostty"));
        await Assert.That(caps.SupportsKittyGraphics).IsTrue();
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Kitty);
    }

    [Test]
    public async Task WindowsTerminal_IsSixel_AndGetsTheOverlay()
    {
        var caps = Detect(("WT_SESSION", "1b2c3d4e-0000-0000-0000-000000000000"));
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Sixel);
        await Assert.That(caps.SupportsSixelOverlay).IsTrue();
    }

    [Test]
    public async Task ADetectedSixelTerminalElsewhereDoesNotGetTheOverlay()
    {
        var caps = Detect(("TERM_PROGRAM", "WezTerm"));
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Sixel);
        await Assert.That(caps.SupportsSixelOverlay).IsFalse();
    }

    [Test]
    public async Task ForcedSixel_GetsTheOverlay()
    {
        var caps = Detect(("SHARPMUTERM_GRAPHICS", "sixel"));
        await Assert.That(caps.SupportsSixelOverlay).IsTrue();
    }

    [Test]
    public async Task Kitty_ViaRio()
    {
        var caps = Detect(("TERM_PROGRAM", "rio"));
        await Assert.That(caps.SupportsKittyGraphics).IsTrue();
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Kitty);
        await Assert.That(caps.SupportsTransparentColours).IsFalse();
    }

    /// <summary>
    /// Per-colour opacity is kitty's color-control escape, and only kitty's: Ghostty shares the graphics
    /// protocol and not this, so it is detected as kitty for pictures and not for transparency.
    /// </summary>
    [Test]
    [Arguments("TERM", "xterm-kitty", true)]
    [Arguments("KITTY_WINDOW_ID", "3", true)]
    [Arguments("TERM", "xterm-ghostty", false)]
    [Arguments("TERM_PROGRAM", "ghostty", false)]
    [Arguments("TERM_PROGRAM", "WezTerm", false)]
    public async Task TransparentColours_KittyOnly(string name, string value, bool expected)
    {
        var caps = Detect((name, value));
        await Assert.That(caps.SupportsTransparentColours).IsEqualTo(expected);
    }

    [Test]
    public async Task Kitty_ViaGhosttyTerm()
    {
        var caps = Detect(("TERM", "xterm-ghostty"));
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Kitty);
    }

    /// <summary>
    /// WezTerm speaks the Kitty protocol's direct placements but not its Unicode placeholders, which is
    /// the only part a compositor can use: placeholder cells would come out as literal glyphs. It is a
    /// Sixel terminal as far as this probe is concerned.
    /// </summary>
    [Test]
    public async Task WezTermIsNotOfferedKittyPlaceholders()
    {
        var caps = Detect(("TERM_PROGRAM", "WezTerm"));
        await Assert.That(caps.SupportsKittyGraphics).IsFalse();
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Sixel);
    }

    [Test]
    public async Task Sixel_ViaTerm()
    {
        var caps = Detect(("TERM", "xterm-sixel"));
        await Assert.That(caps.SupportsSixel).IsTrue();
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Sixel);
    }

    [Test]
    public async Task Sixel_ViaEnvFlag()
    {
        var caps = Detect(("SHARPMUTERM_SIXEL", "1"));
        await Assert.That(caps.SupportsSixel).IsTrue();
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Sixel);
    }

    [Test]
    public async Task Sixel_ViaKnownProgram()
    {
        var caps = Detect(("TERM_PROGRAM", "foot"));
        await Assert.That(caps.SupportsSixel).IsTrue();
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Sixel);
    }

    [Test]
    public async Task Kitty_TakesPrecedenceOverSixel()
    {
        var caps = Detect(("TERM", "xterm-kitty"), ("SHARPMUTERM_SIXEL", "1"));
        await Assert.That(caps.SupportsKittyGraphics).IsTrue();
        await Assert.That(caps.SupportsSixel).IsTrue();
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Kitty);
    }

    [Test]
    [Arguments("none", GraphicsProtocol.None)]
    [Arguments("halfblock", GraphicsProtocol.HalfBlock)]
    [Arguments("half-block", GraphicsProtocol.HalfBlock)]
    [Arguments("sixel", GraphicsProtocol.Sixel)]
    [Arguments("kitty", GraphicsProtocol.Kitty)]
    [Arguments("KITTY", GraphicsProtocol.Kitty)]
    public async Task Override_ForcesProtocol(string value, GraphicsProtocol expected)
    {
        // Even with a Kitty-advertising env, the override must win.
        var caps = Detect(("TERM", "xterm-kitty"), ("SHARPMUTERM_GRAPHICS", value));
        await Assert.That(caps.Protocol).IsEqualTo(expected);
    }

    [Test]
    public async Task Override_Invalid_IsIgnored()
    {
        var caps = Detect(("TERM", "xterm-kitty"), ("SHARPMUTERM_GRAPHICS", "bogus"));
        await Assert.That(caps.Protocol).IsEqualTo(GraphicsProtocol.Kitty);
    }

    [Test]
    public async Task DetectFromEnvironment_DoesNotThrow()
    {
        var caps = CapabilityProbe.DetectFromEnvironment();
        await Assert.That(caps).IsNotNull();
    }
}
