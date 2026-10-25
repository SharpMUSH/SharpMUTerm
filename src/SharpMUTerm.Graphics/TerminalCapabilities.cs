namespace SharpMUTerm.Graphics;

/// <summary>
/// An immutable snapshot of what the host terminal can display. Produced by
/// <see cref="CapabilityProbe"/>. Never assumes more than the environment advertises —
/// a headless sandbox degrades cleanly to <see cref="GraphicsProtocol.None"/>/<see cref="GraphicsProtocol.HalfBlock"/>.
/// </summary>
public sealed class TerminalCapabilities
{
    public TerminalCapabilities(
        GraphicsProtocol protocol,
        bool supportsTrueColor,
        bool supportsKittyGraphics,
        bool supportsSixel,
        bool supportsTransparentColours = false,
        bool isForced = false)
    {
        Protocol = protocol;
        SupportsTrueColor = supportsTrueColor;
        SupportsKittyGraphics = supportsKittyGraphics;
        SupportsSixel = supportsSixel;
        SupportsTransparentColours = supportsTransparentColours;
        IsForced = isForced;
    }

    /// <summary>The best inline-graphics protocol available.</summary>
    public GraphicsProtocol Protocol { get; }

    /// <summary>True when the terminal advertises 24-bit colour (needed for good half-block output).</summary>
    public bool SupportsTrueColor { get; }

    /// <summary>True when the Kitty graphics protocol is available.</summary>
    public bool SupportsKittyGraphics { get; }

    /// <summary>True when Sixel raster graphics are available.</summary>
    public bool SupportsSixel { get; }

    /// <summary>
    /// True when the terminal can be told to draw named background colours translucent — kitty's
    /// <c>transparent_background_color1..7</c>, set through its color-control escape (<c>OSC 21</c>).
    /// Kitty alone: Ghostty speaks kitty's graphics protocol and not this.
    /// </summary>
    public bool SupportsTransparentColours { get; }

    /// <summary>
    /// True when <see cref="Protocol"/> was named by <c>SHARPMUTERM_GRAPHICS</c> (or the config's graphics
    /// override) rather than detected. A host that has its own idea of what the terminal supports should
    /// defer to this: it is the user saying they know better than every probe.
    /// </summary>
    public bool IsForced { get; }

    public override string ToString() =>
        $"{Protocol} (truecolor={SupportsTrueColor}, kitty={SupportsKittyGraphics}, sixel={SupportsSixel}, transparent colours={SupportsTransparentColours}{(IsForced ? ", forced" : "")})";
}
