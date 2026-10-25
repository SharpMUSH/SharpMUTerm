using System.Text;

namespace SharpMUTerm.Graphics;

/// <summary>
/// Encodes an <see cref="IImageSource"/> to a DEC Sixel data string
/// (<c>ESC P q … ESC \</c>). Colours are quantised to a 6-level-per-channel RGB cube
/// (≤216 registers, within Sixel's 256-register budget); each register is declared with
/// <c>#n;2;r;g;b</c> (channels scaled 0-100), then pixels are emitted in six-row bands.
///
/// The implementation favours correctness and determinism (so it can be golden-tested)
/// over compactness — it performs no run-length compression.
/// </summary>
public sealed class SixelEncoder
{
    private const string Intro = "\u001bPq";      // ESC P q : start Sixel, default params.
    private const string Terminator = "\u001b\\"; // ESC \   : String Terminator.

    /// <summary>Encodes the image to a complete Sixel data string.</summary>
    public string Encode(IImageSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var width = image.Width;
        var height = image.Height;

        // Quantise every pixel to a palette index, assigning registers on first appearance.
        var palette = new List<(int R, int G, int B)>();
        var paletteLookup = new Dictionary<int, int>();
        var indices = new int[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = image.GetPixel(x, y);
                var quant = Quantize(pixel);
                var key = (quant.R << 16) | (quant.G << 8) | quant.B;
                if (!paletteLookup.TryGetValue(key, out var index))
                {
                    index = palette.Count;
                    paletteLookup[key] = index;
                    palette.Add(quant);
                }

                indices[(y * width) + x] = index;
            }
        }

        var sb = new StringBuilder();
        sb.Append(Intro);

        // Register declarations, in assignment order.
        for (var i = 0; i < palette.Count; i++)
        {
            var (r, g, b) = palette[i];
            sb.Append('#').Append(i).Append(";2;").Append(r).Append(';').Append(g).Append(';').Append(b);
        }

        var bandCount = (height + 5) / 6;
        for (var band = 0; band < bandCount; band++)
        {
            var bandTop = band * 6;
            var bandRows = Math.Min(6, height - bandTop);

            // Which registers actually appear in this band (in assignment order).
            var colorsInBand = new SortedSet<int>();
            for (var row = 0; row < bandRows; row++)
            {
                var y = bandTop + row;
                for (var x = 0; x < width; x++)
                {
                    colorsInBand.Add(indices[(y * width) + x]);
                }
            }

            var first = true;
            foreach (var color in colorsInBand)
            {
                if (!first)
                {
                    // Carriage return: overlay the next colour on the same band.
                    sb.Append('$');
                }

                first = false;

                sb.Append('#').Append(color);
                for (var x = 0; x < width; x++)
                {
                    var mask = 0;
                    for (var row = 0; row < bandRows; row++)
                    {
                        var y = bandTop + row;
                        if (indices[(y * width) + x] == color)
                        {
                            mask |= 1 << row;
                        }
                    }

                    sb.Append((char)(63 + mask));
                }
            }

            // Graphics newline between bands (not after the final band).
            if (band < bandCount - 1)
            {
                sb.Append('-');
            }
        }

        sb.Append(Terminator);
        return sb.ToString();
    }

    /// <summary>
    /// Encodes the image for a terminal to draw over cells that already hold something: up to 256
    /// registers chosen from the image's own colours, run-length compressed, with the raster size declared
    /// and unset pixels left transparent (<c>P2=1</c>) rather than painted in the terminal's background.
    /// </summary>
    /// <remarks>
    /// <see cref="Encode"/> is the deterministic, uncompressed form and stays as it is for its golden
    /// tests. This one is what goes on the wire: a 400×400 picture is roughly a fifth the size, and an
    /// adaptive palette is the difference between a photograph and a poster of one. Colours are bucketed
    /// at five bits a channel, the most frequent 256 buckets become registers, and every other bucket takes
    /// its nearest register (worked out once per bucket, not once per pixel).
    /// </remarks>
    public static string EncodeCompact(IImageSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var width = image.Width;
        var height = image.Height;
        var buckets = new int[width * height];
        var counts = new Dictionary<int, int>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = image.GetPixel(x, y);
                var bucket = ((p.R >> 3) << 10) | ((p.G >> 3) << 5) | (p.B >> 3);
                buckets[(y * width) + x] = bucket;
                counts[bucket] = counts.TryGetValue(bucket, out var n) ? n + 1 : 1;
            }
        }

        var palette = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(256).Select(kv => kv.Key).ToArray();
        var registerOf = new Dictionary<int, int>(counts.Count);
        for (var i = 0; i < palette.Length; i++)
        {
            registerOf[palette[i]] = i;
        }

        int Register(int bucket)
        {
            if (registerOf.TryGetValue(bucket, out var known))
            {
                return known;
            }

            var best = 0;
            var bestDistance = int.MaxValue;
            for (var i = 0; i < palette.Length; i++)
            {
                var dr = ((palette[i] >> 10) & 31) - ((bucket >> 10) & 31);
                var dg = ((palette[i] >> 5) & 31) - ((bucket >> 5) & 31);
                var db = (palette[i] & 31) - (bucket & 31);
                var distance = (dr * dr) + (dg * dg) + (db * db);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            registerOf[bucket] = best;
            return best;
        }

        var indices = new int[buckets.Length];
        for (var i = 0; i < buckets.Length; i++)
        {
            indices[i] = Register(buckets[i]);
        }

        var sb = new StringBuilder(width * height / 2);
        sb.Append("\u001bP0;1;0q");
        sb.Append('"').Append("1;1;").Append(width).Append(';').Append(height);
        for (var i = 0; i < palette.Length; i++)
        {
            static int Percent(int fiveBits) => ((fiveBits << 3) | (fiveBits >> 2)) * 100 / 255;
            sb.Append('#').Append(i).Append(";2;")
                .Append(Percent((palette[i] >> 10) & 31)).Append(';')
                .Append(Percent((palette[i] >> 5) & 31)).Append(';')
                .Append(Percent(palette[i] & 31));
        }

        var masks = new int[width];
        var bandCount = (height + 5) / 6;
        for (var band = 0; band < bandCount; band++)
        {
            var bandTop = band * 6;
            var bandRows = Math.Min(6, height - bandTop);
            var colorsInBand = new SortedSet<int>();
            for (var row = 0; row < bandRows; row++)
            {
                for (var x = 0; x < width; x++)
                {
                    colorsInBand.Add(indices[((bandTop + row) * width) + x]);
                }
            }

            var first = true;
            foreach (var color in colorsInBand)
            {
                if (!first)
                {
                    sb.Append('$');
                }

                first = false;
                for (var x = 0; x < width; x++)
                {
                    var mask = 0;
                    for (var row = 0; row < bandRows; row++)
                    {
                        if (indices[((bandTop + row) * width) + x] == color)
                        {
                            mask |= 1 << row;
                        }
                    }

                    masks[x] = mask;
                }

                // Trailing empty columns are not sent: a colour's run ends where it last appears.
                var end = width;
                while (end > 0 && masks[end - 1] == 0)
                {
                    end--;
                }

                sb.Append('#').Append(color);
                for (var x = 0; x < end;)
                {
                    var run = 1;
                    while (x + run < end && masks[x + run] == masks[x])
                    {
                        run++;
                    }

                    var glyph = (char)(63 + masks[x]);
                    if (run > 3)
                    {
                        sb.Append('!').Append(run).Append(glyph);
                    }
                    else
                    {
                        sb.Append(glyph, run);
                    }

                    x += run;
                }
            }

            if (band < bandCount - 1)
            {
                sb.Append('-');
            }
        }

        sb.Append(Terminator);
        return sb.ToString();
    }

    /// <summary>
    /// Quantises an RGBA pixel to the 6-level RGB cube and returns each channel scaled to
    /// the Sixel 0-100 range (levels 0..5 → 0,20,40,60,80,100).
    /// </summary>
    private static (int R, int G, int B) Quantize(Rgba32 pixel)
    {
        static int Channel(byte value)
        {
            var level = value * 6 / 256; // 0..5
            return level * 20;           // 0,20,40,60,80,100
        }

        return (Channel(pixel.R), Channel(pixel.G), Channel(pixel.B));
    }
}
