using System.Globalization;
using System.Text;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui;

/// <summary>
/// The pane rows an inline picture is drawn as. Both forms are ordinary markup lines, which is the
/// whole design: a picture in an output pane is a run of rows in that pane's line buffer, so it scrolls,
/// clips, freezes and trims exactly like the text around it, and the pane never has to hold anything
/// but a <c>MarkupControl</c>.
/// <para>
/// These rows are written here and <b>never</b> through <see cref="MarkupFormatter"/>. Its legibility
/// floor moves a foreground that is too dark for its plane — and a Kitty placeholder's foreground is
/// not a colour at all but the image id, so a "corrected" one names a different image. Half-block
/// pixels are not text either, and lifting them would repaint the picture.
/// </para>
/// </summary>
internal static class InlineImageRows
{
    /// <summary>
    /// Kitty Unicode placeholders: each cell is <c>U+10EEEE</c> with its row and column as combining
    /// diacritics, and the image id in the foreground colour. The terminal draws the picture
    /// transmitted under that id over these cells.
    /// </summary>
    public static IReadOnlyList<string> Kitty(int imageId, int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(imageId, 0xFFFFFF);
        var limit = KittyGraphicsProtocol.RowColumnDiacritics.Length;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, limit);

        var placeholder = char.ConvertFromUtf32(KittyGraphicsProtocol.PlaceholderCodePoint);
        var colour = Hex((byte)(imageId >> 16), (byte)(imageId >> 8), (byte)imageId);
        var result = new string[rows];
        for (var row = 0; row < rows; row++)
        {
            var rowMark = char.ConvertFromUtf32(KittyGraphicsProtocol.RowColumnDiacritics[row]);
            var sb = new StringBuilder(columns * 6 + 16);
            sb.Append('[').Append(colour).Append(']');
            for (var col = 0; col < columns; col++)
            {
                sb.Append(placeholder)
                    .Append(rowMark)
                    .Append(char.ConvertFromUtf32(KittyGraphicsProtocol.RowColumnDiacritics[col]));
            }

            result[row] = sb.Append("[/]").ToString();
        }

        return result;
    }

    /// <summary>
    /// Upper half-blocks: two pixels a cell, the top one in the foreground and the bottom one in the
    /// background. <paramref name="pixel"/> is read over <paramref name="columns"/> ×
    /// 2·<paramref name="rows"/>; a run of identical cells shares one tag.
    /// </summary>
    public static IReadOnlyList<string> HalfBlock(Func<int, int, (byte R, byte G, byte B)> pixel, int columns, int rows)
    {
        ArgumentNullException.ThrowIfNull(pixel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);

        var result = new string[rows];
        for (var row = 0; row < rows; row++)
        {
            var sb = new StringBuilder(columns * 4);
            string? open = null;
            for (var col = 0; col < columns; col++)
            {
                var (tr, tg, tb) = pixel(col, row * 2);
                var (br, bg, bb) = pixel(col, row * 2 + 1);
                var tag = $"{Hex(tr, tg, tb)} on {Hex(br, bg, bb)}";
                if (tag != open)
                {
                    if (open is not null)
                    {
                        sb.Append("[/]");
                    }

                    sb.Append('[').Append(tag).Append(']');
                    open = tag;
                }

                sb.Append(HalfBlockRenderer.UpperHalfBlock);
            }

            if (open is not null)
            {
                sb.Append("[/]");
            }

            result[row] = sb.ToString();
        }

        return result;
    }

    private static string Hex(byte r, byte g, byte b) =>
        string.Create(CultureInfo.InvariantCulture, $"#{r:x2}{g:x2}{b:x2}");
}
