namespace SharpMUTerm.Core.Avatars;

/// <summary>
/// The square of a picture kept for an avatar: its centre as a share of the picture's width and height, and its side as
/// a share of the picture's shorter side. <see cref="Centre"/> (the whole shorter side, centred) is what a picture with no
/// crop of its own gets.
/// </summary>
public readonly record struct AvatarCrop(double CentreX, double CentreY, double Side)
{
    /// <summary>The largest centred square.</summary>
    public static AvatarCrop Centre { get; } = new(0.5, 0.5, 1);

    /// <summary>
    /// The crop in pixels of a <paramref name="width"/>×<paramref name="height"/> picture: left, top and side, the side at
    /// least one pixel and the square moved, never shrunk, to lie inside the picture.
    /// </summary>
    public (int Left, int Top, int Side) In(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        var shorter = Math.Min(width, height);
        var share = double.IsFinite(Side) ? Math.Clamp(Side, 0, 1) : 1;
        var side = Math.Clamp((int)Math.Round(shorter * share), 1, shorter);
        static double Finite(double v) => double.IsFinite(v) ? v : 0.5;
        var left = (int)Math.Round((Finite(CentreX) * width) - (side / 2.0));
        var top = (int)Math.Round((Finite(CentreY) * height) - (side / 2.0));
        return (Math.Clamp(left, 0, width - side), Math.Clamp(top, 0, height - side), side);
    }
}
