namespace SharpMUTerm.Tui;

/// <summary>
/// Made-up pictures for the <c>avatars</c> views: a portrait per demo character and an emblem per world, drawn here
/// and handed over as BMP bytes, so the demo reads no file and fetches nothing. The sources are paths under
/// <see cref="Root"/>, which exists nowhere; only this reader answers for them.
/// </summary>
internal static class DemoAvatars
{
    /// <summary>The folder the demo's sources name.</summary>
    public const string Root = "/demo/avatars/";

    /// <summary>The source for <paramref name="name"/>.</summary>
    public static string For(string name) => Root + name.ToLowerInvariant() + ".png";

    /// <summary>Answers for the demo's own sources, and for nothing else.</summary>
    public static Task<byte[]?> ReadAsync(string source, CancellationToken cancellationToken) =>
        Task.FromResult(source.StartsWith(Root, StringComparison.Ordinal) ? Draw(Path.GetFileNameWithoutExtension(source)) : null);

    private static byte[]? Draw(string name) => name switch
    {
        "corvid" => Portrait((38, 52, 92), (226, 232, 244), (24, 26, 34), beak: true),
        "rookery" => Portrait((92, 54, 40), (238, 210, 180), (60, 40, 30), beak: false),
        "aetherfall" => Emblem((22, 70, 84), (120, 220, 200)),
        "grapevine" => Emblem((70, 34, 82), (210, 160, 240)),
        _ => null,
    };

    private const int Side = 96;

    /// <summary>A head and shoulders on a gradient: the shape of a portrait, at the size a sidebar shows.</summary>
    private static byte[] Portrait((int R, int G, int B) ground, (int R, int G, int B) skin, (int R, int G, int B) hair, bool beak)
    {
        return Bitmap((x, y) =>
        {
            var t = y / (double)Side;
            var colour = Mix(ground, (ground.R / 3, ground.G / 3, ground.B / 3), t);
            var head = Distance(x, y, 48, 40) < 21;
            var shoulders = y > 70 && Distance(x, y, 48, 112) < 46;
            if (shoulders)
            {
                colour = Mix(hair, ground, 0.25);
            }

            if (head)
            {
                colour = y < 30 && Distance(x, y, 48, 34) > 13 ? hair : skin;
                if (Distance(x, y, 40, 40) < 2.5 || Distance(x, y, 56, 40) < 2.5)
                {
                    colour = (20, 20, 28);
                }

                if (beak && y > 44 && y < 54 && Math.Abs(x - 48) < (54 - y) * 0.6)
                {
                    colour = (232, 170, 60);
                }
            }

            return colour;
        });
    }

    /// <summary>A banded sun over hills: a world's mark.</summary>
    private static byte[] Emblem((int R, int G, int B) ground, (int R, int G, int B) mark)
    {
        return Bitmap((x, y) =>
        {
            var colour = Mix(ground, (ground.R / 2, ground.G / 2, ground.B / 2), y / (double)Side);
            if (Distance(x, y, 48, 46) < 22 && (y / 6) % 2 == 0)
            {
                colour = mark;
            }

            if (y > 62 + (8 * Math.Sin(x / 14.0)))
            {
                colour = Mix(mark, ground, 0.6);
            }

            return colour;
        });
    }

    private static double Distance(int x, int y, double cx, double cy) => Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));

    private static (int R, int G, int B) Mix((int R, int G, int B) a, (int R, int G, int B) b, double t) =>
        ((int)(a.R + ((b.R - a.R) * t)), (int)(a.G + ((b.G - a.G) * t)), (int)(a.B + ((b.B - a.B) * t)));

    /// <summary>A 24-bit BMP of <see cref="Side"/>² pixels, which every decoder the client has reads.</summary>
    private static byte[] Bitmap(Func<int, int, (int R, int G, int B)> pixel)
    {
        const int row = Side * 3; // 288, already a multiple of four
        const int data = row * Side;
        var bytes = new byte[54 + data];
        void Int(int at, int value) => BitConverter.TryWriteBytes(bytes.AsSpan(at, 4), value);
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        Int(2, bytes.Length);
        Int(10, 54);
        Int(14, 40);
        Int(18, Side);
        Int(22, Side);
        bytes[26] = 1;
        bytes[28] = 24;
        Int(34, data);
        for (var y = 0; y < Side; y++)
        {
            for (var x = 0; x < Side; x++)
            {
                var (r, g, b) = pixel(x, y);
                var at = 54 + ((Side - 1 - y) * row) + (x * 3); // bottom-up
                bytes[at] = (byte)Math.Clamp(b, 0, 255);
                bytes[at + 1] = (byte)Math.Clamp(g, 0, 255);
                bytes[at + 2] = (byte)Math.Clamp(r, 0, 255);
            }
        }

        return bytes;
    }
}
