using System.Text;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Graphics;

/// <summary>Pixel format of a payload handed to the Kitty encoder.</summary>
public enum KittyImageFormat
{
    /// <summary>Raw 32-bit RGBA pixels (Kitty <c>f=32</c>).</summary>
    Rgba,

    /// <summary>A complete PNG file (Kitty <c>f=100</c>).</summary>
    Png,
}

/// <summary>
/// Deterministic encoder for the
/// <see href="https://sw.kovidgoyal.net/kitty/graphics-protocol/">Kitty graphics protocol</see>.
///
/// Every method returns the escape-sequence string(s); nothing is written to any console,
/// which keeps the encoder pure and golden-testable. The framing for one transmission is
/// <c>ESC _G &lt;controls&gt; ; &lt;base64-payload&gt; ESC \</c>. Large payloads are split into
/// chunks of at most 4096 base64 characters, each carrying <c>m=1</c> except the final
/// chunk which carries <c>m=0</c>.
/// </summary>
public sealed class KittyGraphicsProtocol
{
    /// <summary>The Unicode placeholder code point (<c>U+10EEEE</c>) that anchors an image to text cells.</summary>
    public const int PlaceholderCodePoint = 0x10EEEE;

    /// <summary>Maximum base64 characters per Kitty transmission chunk, per the spec.</summary>
    public const int MaxChunkBase64Length = 4096;

    private const string ApcStart = "\u001b_G"; // ESC _ G  (Application Programming Command)
    private const string St = "\u001b\\";       // ESC \    (String Terminator)

    /// <summary>
    /// The Kitty "row/column diacritics" table: combining marks whose position in this
    /// list encodes a 0-based row or column index. All 297 entries of kitty's
    /// <c>gen/rowcolumn-diacritics.txt</c>, in its order, so a placeholder grid may be up to
    /// 297 cells on either side.
    /// </summary>
    public static readonly int[] RowColumnDiacritics =
    {
        0x0305, 0x030D, 0x030E, 0x0310, 0x0312, 0x033D, 0x033E, 0x033F,
        0x0346, 0x034A, 0x034B, 0x034C, 0x0350, 0x0351, 0x0352, 0x0357,
        0x035B, 0x0363, 0x0364, 0x0365, 0x0366, 0x0367, 0x0368, 0x0369,
        0x036A, 0x036B, 0x036C, 0x036D, 0x036E, 0x036F, 0x0483, 0x0484,
        0x0485, 0x0486, 0x0487, 0x0592, 0x0593, 0x0594, 0x0595, 0x0597,
        0x0598, 0x0599, 0x059C, 0x059D, 0x059E, 0x059F, 0x05A0, 0x05A1,
        0x05A8, 0x05A9, 0x05AB, 0x05AC, 0x05AF, 0x05C4, 0x0610, 0x0611,
        0x0612, 0x0613, 0x0614, 0x0615, 0x0616, 0x0617, 0x0657, 0x0658,
        0x0659, 0x065A, 0x065B, 0x065D, 0x065E, 0x06D6, 0x06D7, 0x06D8,
        0x06D9, 0x06DA, 0x06DB, 0x06DC, 0x06DF, 0x06E0, 0x06E1, 0x06E2,
        0x06E4, 0x06E7, 0x06E8, 0x06EB, 0x06EC, 0x0730, 0x0732, 0x0733,
        0x0735, 0x0736, 0x073A, 0x073D, 0x073F, 0x0740, 0x0741, 0x0743,
        0x0745, 0x0747, 0x0749, 0x074A, 0x07EB, 0x07EC, 0x07ED, 0x07EE,
        0x07EF, 0x07F0, 0x07F1, 0x07F3, 0x0816, 0x0817, 0x0818, 0x0819,
        0x081B, 0x081C, 0x081D, 0x081E, 0x081F, 0x0820, 0x0821, 0x0822,
        0x0823, 0x0825, 0x0826, 0x0827, 0x0829, 0x082A, 0x082B, 0x082C,
        0x082D, 0x0951, 0x0953, 0x0954, 0x0F82, 0x0F83, 0x0F86, 0x0F87,
        0x135D, 0x135E, 0x135F, 0x17DD, 0x193A, 0x1A17, 0x1A75, 0x1A76,
        0x1A77, 0x1A78, 0x1A79, 0x1A7A, 0x1A7B, 0x1A7C, 0x1B6B, 0x1B6D,
        0x1B6E, 0x1B6F, 0x1B70, 0x1B71, 0x1B72, 0x1B73, 0x1CD0, 0x1CD1,
        0x1CD2, 0x1CDA, 0x1CDB, 0x1CE0, 0x1DC0, 0x1DC1, 0x1DC3, 0x1DC4,
        0x1DC5, 0x1DC6, 0x1DC7, 0x1DC8, 0x1DC9, 0x1DCB, 0x1DCC, 0x1DD1,
        0x1DD2, 0x1DD3, 0x1DD4, 0x1DD5, 0x1DD6, 0x1DD7, 0x1DD8, 0x1DD9,
        0x1DDA, 0x1DDB, 0x1DDC, 0x1DDD, 0x1DDE, 0x1DDF, 0x1DE0, 0x1DE1,
        0x1DE2, 0x1DE3, 0x1DE4, 0x1DE5, 0x1DE6, 0x1DFE, 0x20D0, 0x20D1,
        0x20D4, 0x20D5, 0x20D6, 0x20D7, 0x20DB, 0x20DC, 0x20E1, 0x20E7,
        0x20E9, 0x20F0, 0x2CEF, 0x2CF0, 0x2CF1, 0x2DE0, 0x2DE1, 0x2DE2,
        0x2DE3, 0x2DE4, 0x2DE5, 0x2DE6, 0x2DE7, 0x2DE8, 0x2DE9, 0x2DEA,
        0x2DEB, 0x2DEC, 0x2DED, 0x2DEE, 0x2DEF, 0x2DF0, 0x2DF1, 0x2DF2,
        0x2DF3, 0x2DF4, 0x2DF5, 0x2DF6, 0x2DF7, 0x2DF8, 0x2DF9, 0x2DFA,
        0x2DFB, 0x2DFC, 0x2DFD, 0x2DFE, 0x2DFF, 0xA66F, 0xA67C, 0xA67D,
        0xA6F0, 0xA6F1, 0xA8E0, 0xA8E1, 0xA8E2, 0xA8E3, 0xA8E4, 0xA8E5,
        0xA8E6, 0xA8E7, 0xA8E8, 0xA8E9, 0xA8EA, 0xA8EB, 0xA8EC, 0xA8ED,
        0xA8EE, 0xA8EF, 0xA8F0, 0xA8F1, 0xAAB0, 0xAAB2, 0xAAB3, 0xAAB7,
        0xAAB8, 0xAABE, 0xAABF, 0xAAC1, 0xFE20, 0xFE21, 0xFE22, 0xFE23,
        0xFE24, 0xFE25, 0xFE26, 0x10A0F, 0x10A38, 0x1D185, 0x1D186, 0x1D187,
        0x1D188, 0x1D189, 0x1D1AA, 0x1D1AB, 0x1D1AC, 0x1D1AD, 0x1D242, 0x1D243,
        0x1D244,
    };

    /// <summary>
    /// Transmits an image and displays it in one action (<c>a=T</c>). Returns the full
    /// escape sequence, split into <c>m=1</c>/<c>m=0</c> chunks when the base64 payload
    /// exceeds <see cref="MaxChunkBase64Length"/>.
    /// </summary>
    /// <param name="imageId">Client-assigned image id (<c>i=</c>).</param>
    /// <param name="payload">Raw RGBA pixels or a PNG file, per <paramref name="format"/>.</param>
    /// <param name="width">Source pixel width (<c>s=</c>).</param>
    /// <param name="height">Source pixel height (<c>v=</c>).</param>
    /// <param name="format">Payload format.</param>
    public string TransmitAndDisplay(
        int imageId,
        ReadOnlySpan<byte> payload,
        int width,
        int height,
        KittyImageFormat format)
    {
        if (imageId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageId), imageId, "Image id must be positive.");
        }

        var formatCode = format == KittyImageFormat.Png ? 100 : 32;
        var base64 = Convert.ToBase64String(payload);

        var builder = new StringBuilder();
        var chunkCount = Math.Max(1, (base64.Length + MaxChunkBase64Length - 1) / MaxChunkBase64Length);

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var start = chunkIndex * MaxChunkBase64Length;
            var length = Math.Min(MaxChunkBase64Length, base64.Length - start);
            var chunk = base64.Substring(start, length);
            var isLast = chunkIndex == chunkCount - 1;

            builder.Append(ApcStart);

            if (chunkIndex == 0)
            {
                // The first chunk carries the full control set.
                builder.Append("a=T,f=").Append(formatCode)
                    .Append(",i=").Append(imageId)
                    .Append(",s=").Append(width)
                    .Append(",v=").Append(height);
                builder.Append(",m=").Append(isLast ? '0' : '1');
            }
            else
            {
                // Continuation chunks only carry the more flag.
                builder.Append("m=").Append(isLast ? '0' : '1');
            }

            builder.Append(';').Append(chunk).Append(St);
        }

        return builder.ToString();
    }

    /// <summary>Deletes an image by id (<c>a=d,i=&lt;id&gt;</c>).</summary>
    public string Delete(int imageId)
    {
        if (imageId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageId), imageId, "Image id must be positive.");
        }

        return $"{ApcStart}a=d,i={imageId};{St}";
    }

    /// <summary>
    /// Builds the Unicode-placeholder grid for a previously transmitted image. Each cell
    /// is the placeholder rune <c>U+10EEEE</c> followed by two combining diacritics that
    /// encode its (row, column); the image id is carried in the foreground colour of every
    /// cell. Rendering this grid causes the terminal to composite the image over those
    /// real text cells. Returns one <see cref="StyledLine"/> per row.
    /// </summary>
    public IReadOnlyList<StyledLine> BuildPlaceholder(int imageId, int cols, int rows)
    {
        if (imageId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageId), imageId, "Image id must be positive.");
        }

        if (cols <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cols), cols, "Columns must be positive.");
        }

        if (rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), rows, "Rows must be positive.");
        }

        if (rows > RowColumnDiacritics.Length || cols > RowColumnDiacritics.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rows),
                $"Placeholder grid up to {RowColumnDiacritics.Length}x{RowColumnDiacritics.Length} is supported.");
        }

        // The placeholder carries the image id in a 24-bit foreground colour, so ids above
        // 0xFFFFFF cannot round-trip. Reject them rather than silently truncating.
        if (imageId is < 0 or > 0xFFFFFF)
        {
            throw new ArgumentOutOfRangeException(
                nameof(imageId), imageId, "Placeholder image id must fit in 24 bits (0-0xFFFFFF).");
        }

        // Carry the 24-bit image id in the foreground colour, per the Kitty spec.
        var idColor = TerminalColor.FromRgb(
            (byte)((imageId >> 16) & 0xFF),
            (byte)((imageId >> 8) & 0xFF),
            (byte)(imageId & 0xFF));
        var style = TextStyle.Default.WithForeground(idColor);

        var lines = new StyledLine[rows];
        for (var row = 0; row < rows; row++)
        {
            var spans = new StyledSpan[cols];
            for (var col = 0; col < cols; col++)
            {
                var cell = new StringBuilder(4);
                cell.Append(char.ConvertFromUtf32(PlaceholderCodePoint));
                cell.Append(char.ConvertFromUtf32(RowColumnDiacritics[row]));
                cell.Append(char.ConvertFromUtf32(RowColumnDiacritics[col]));
                spans[col] = new StyledSpan(cell.ToString(), style);
            }

            lines[row] = new StyledLine(spans);
        }

        return lines;
    }
}
