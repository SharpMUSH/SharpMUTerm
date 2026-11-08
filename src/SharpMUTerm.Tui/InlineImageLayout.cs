using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// The cell box an MXP <c>&lt;IMAGE&gt;</c> occupies in an output pane: the picture's own size, then
/// the world's <c>W=</c>/<c>H=</c> hints, then the room there is. Pure, so the sizing is testable
/// without a terminal.
/// <para>
/// Desktop hosts supply measured logical-pixel cells. Without those, a cell is taken to be
/// <see cref="CellPixelWidth"/> × <see cref="CellPixelHeight"/> pixels. The
/// client cannot ask the terminal (the framework owns the input stream a <c>CSI 16 t</c> reply would
/// arrive on), so those hosts retain the established 1:2 fallback. Desktop sizing uses the measured
/// aspect ratio instead; the final box is rounded to whole cells.
/// </para>
/// </summary>
internal static class InlineImageLayout
{
    /// <summary>Assumed width of one terminal cell, in pixels.</summary>
    public const int CellPixelWidth = 8;

    /// <summary>Assumed height of one terminal cell, in pixels.</summary>
    public const int CellPixelHeight = 16;

    /// <summary>
    /// Tallest an image may be, so one picture cannot push a screenful of conversation out of view.
    /// A <c>%</c> height is a share of this, since the spec leaves "percentage of what" open.
    /// </summary>
    public const int MaxRows = 24;

    /// <summary>
    /// Fits a decoded picture. Both hints given stretches it, as the spec says; one hint scales the
    /// other edge to keep the picture's own shape; none uses the picture's size. The result is then
    /// shrunk, shape kept, until it fits <paramref name="availableColumns"/> and <paramref name="maxRows"/>.
    /// </summary>
    public static WebImageLayout.CellBox Fit(
        int pixelWidth,
        int pixelHeight,
        ImageExtent? width,
        ImageExtent? height,
        int availableColumns,
        int maxRows = MaxRows,
        ImageCellSize? cellSize = null)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0 || availableColumns <= 0 || maxRows <= 0)
        {
            return new WebImageLayout.CellBox(0, 0);
        }

        var cw = cellSize?.Width is > 0 and var measuredWidth && double.IsFinite(measuredWidth) ? measuredWidth : CellPixelWidth;
        var ch = cellSize?.Height is > 0 and var measuredHeight && double.IsFinite(measuredHeight) ? measuredHeight : CellPixelHeight;
        double columns = Math.Ceiling(pixelWidth / cw);
        double rows = Math.Ceiling(pixelHeight / ch);
        var aspect = cellSize is null ? rows / columns : (pixelHeight / ch) / (pixelWidth / cw);

        var hintedColumns = Columns(width, availableColumns, cw);
        var hintedRows = Rows(height, maxRows, ch);
        if (hintedColumns is { } c && hintedRows is { } r)
        {
            columns = c;
            rows = r;
        }
        else if (hintedColumns is { } onlyColumns)
        {
            columns = onlyColumns;
            rows = onlyColumns * aspect;
        }
        else if (hintedRows is { } onlyRows)
        {
            rows = onlyRows;
            columns = onlyRows / aspect;
        }

        var scale = Math.Min(1.0, Math.Min(availableColumns / columns, maxRows / rows));
        return new WebImageLayout.CellBox(
            Math.Clamp((int)Math.Round(columns * scale, MidpointRounding.AwayFromZero), 1, availableColumns),
            Math.Clamp((int)Math.Round(rows * scale, MidpointRounding.AwayFromZero), 1, maxRows));
    }

    private static double? Columns(ImageExtent? extent, int available, double cellWidth) => extent switch
    {
        { Unit: ImageExtentUnit.Pixels } e => Math.Ceiling(e.Value / cellWidth),
        { Unit: ImageExtentUnit.Characters } e => e.Value,
        { Unit: ImageExtentUnit.Percent } e => Math.Max(1, available * e.Value / 100.0),
        _ => null,
    };

    private static double? Rows(ImageExtent? extent, int maxRows, double cellHeight) => extent switch
    {
        { Unit: ImageExtentUnit.Pixels } e => Math.Ceiling(e.Value / cellHeight),
        { Unit: ImageExtentUnit.Characters } e => e.Value,
        { Unit: ImageExtentUnit.Percent } e => Math.Max(1, maxRows * e.Value / 100.0),
        _ => null,
    };
}

/// <summary>One display cell in logical pixels; null metrics use the terminal fallback.</summary>
internal sealed record ImageCellSize(double Width, double Height);
