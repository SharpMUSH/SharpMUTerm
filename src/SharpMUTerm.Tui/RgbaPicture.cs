using SharpConsoleUI.Imaging;
using SharpMUTerm.Core.Avatars;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SharpMUTerm.Tui;

/// <summary>Straight RGBA pixels retained until Kitty PNG transmission; RGB is only a fallback.</summary>
internal sealed record RgbaPicture(int Width, int Height, byte[] Data)
{
    public PixelBuffer Pixels => PixelBuffer.FromRgba32Bytes(Data, Width, Height);

    public byte[] Png => PngWriter.Rgba(Width, Height, Data);

    public RgbaPicture Resize(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return this;
        }
        using var image = Image.LoadPixelData<Rgba32>(Data, Width, Height);
        // Premultiplied interpolation prevents hidden RGB under transparent pixels bleeding into edges.
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(width, height), Mode = ResizeMode.Stretch, PremultiplyAlpha = true,
        }));
        var data = new byte[width * height * 4];
        image.CopyPixelDataTo(data);
        return new RgbaPicture(width, height, data);
    }
}
