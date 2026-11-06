namespace SharpMUTerm.Core.Avatars;

/// <summary>
/// The window's crop step as numbers: a <see cref="Width"/>×<see cref="Height"/> picture under a circle
/// <see cref="Diameter"/> across. At zoom 1 the picture's shorter side fills the circle; the picture is panned under the
/// circle and never leaves a gap inside it. <see cref="Crop"/> is the square the circle covers, as an
/// <see cref="AvatarCrop"/>, so the copy kept is the circle's square at any resolution.
/// </summary>
public sealed class AvatarCropStage
{
    /// <summary>The closest zoom: the circle then covers a quarter of the shorter side.</summary>
    public const double MaxZoom = 4;

    public AvatarCropStage(double width, double height, double diameter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(diameter, 1);
        Width = width;
        Height = height;
        Diameter = diameter;
        Zoom = 1;
        Left = (diameter - ImageWidth) / 2;
        Top = (diameter - ImageHeight) / 2;
    }

    public double Width { get; }

    public double Height { get; }

    public double Diameter { get; }

    /// <summary>1 to <see cref="MaxZoom"/>.</summary>
    public double Zoom { get; private set; }

    /// <summary>Where the picture's top-left corner is, from the circle's, in stage pixels (zero or less).</summary>
    public double Left { get; private set; }

    /// <inheritdoc cref="Left"/>
    public double Top { get; private set; }

    /// <summary>Stage pixels per picture pixel.</summary>
    public double Scale => Diameter / Math.Min(Width, Height) * Zoom;

    public double ImageWidth => Width * Scale;

    public double ImageHeight => Height * Scale;

    /// <summary>Moves the picture by <paramref name="dx"/>, <paramref name="dy"/> stage pixels, as far as it can go.</summary>
    public void Pan(double dx, double dy)
    {
        Left = Clamp(Left + dx, ImageWidth);
        Top = Clamp(Top + dy, ImageHeight);
    }

    /// <summary>Zooms to <paramref name="zoom"/> (clamped) about the circle's centre.</summary>
    public void ZoomTo(double zoom)
    {
        var z = double.IsFinite(zoom) ? Math.Clamp(zoom, 1, MaxZoom) : 1;
        var half = Diameter / 2;
        var centreX = (half - Left) / Scale;
        var centreY = (half - Top) / Scale;
        Zoom = z;
        Left = Clamp(half - (centreX * Scale), ImageWidth);
        Top = Clamp(half - (centreY * Scale), ImageHeight);
    }

    /// <summary>The square under the circle.</summary>
    public AvatarCrop Crop
    {
        get
        {
            var side = Diameter / Scale;
            var left = -Left / Scale;
            var top = -Top / Scale;
            return new AvatarCrop((left + (side / 2)) / Width, (top + (side / 2)) / Height, 1 / Zoom);
        }
    }

    private double Clamp(double offset, double extent) => Math.Clamp(offset, Math.Min(0, Diameter - extent), 0);
}
