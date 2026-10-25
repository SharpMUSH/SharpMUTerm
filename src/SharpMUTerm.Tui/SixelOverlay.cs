using System.Text;
using SharpConsoleUI.Imaging;
using SharpConsoleUI.Layout;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui;

/// <summary>
/// Draws a picture's half-block rows again as Sixel, on top of them, after the frame that painted them.
/// <para>
/// The compositor cannot hold Sixel: it is pixels the terminal draws at the cursor, outside the cells, and
/// a cell-filling renderer has nothing it could write that becomes one. What it can do is leave the
/// picture's half-block rows where they are, and this writes the Sixel over them once the frame is out.
/// The terminal keeps the image until the cells under it are written again, and the framework only writes
/// cells that changed, so a picture that sits still is sent once.
/// </para>
/// <para>
/// Where a picture is on screen is read off the frame rather than worked out from the layout: each row is
/// a run of <c>▀</c> cells whose colours are exactly the picture's, so a row is found wherever it was
/// painted — after a scroll, a wrap, a split or a freeze — with no buffer index to keep in step. A row the
/// frame does not show exactly (a selection over it, a pane too narrow for it) is not found, and keeps its
/// half-blocks.
/// </para>
/// <para>
/// Sent again when the rows it covers changed in any way (a change anywhere on a row may make the driver
/// rewrite the span across the picture), when it moved, and after the main window has been covered by
/// another one or the screen was resized. This is a workaround and the risks are named: anything that
/// rewrites those cells without this seeing the frame erases the image, and a busy row redraws it.
/// </para>
/// </summary>
internal sealed class SixelOverlay
{
    /// <summary>Pictures remembered at once; an older one keeps its half-blocks.</summary>
    public const int Capacity = 64;

    private const char UpperHalf = '▀';

    private readonly int _cellWidth;
    private readonly int _cellHeight;
    private readonly LinkedList<Picture> _pictures = new();
    private readonly Dictionary<(uint Top, uint Bottom), List<(Picture Picture, int Row)>> _byFirstCell = new();
    private Dictionary<(Picture, int X, int Y, int FirstRow, int Rows), long> _shown = new();
    private (int Width, int Height) _screen;

    /// <param name="cellWidth">Sixel pixels per cell across. Windows Terminal scales Sixel as if a cell were 10×20.</param>
    /// <param name="cellHeight">Sixel pixels per cell down.</param>
    public SixelOverlay(int cellWidth = 10, int cellHeight = 20)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellHeight);
        _cellWidth = cellWidth;
        _cellHeight = cellHeight;
    }

    /// <summary>Pictures currently remembered.</summary>
    public int Count => _pictures.Count;

    /// <summary>
    /// Remembers a picture drawn as half-blocks. <paramref name="cells"/> is what the rows were made from
    /// (one pixel across and two down per cell), <paramref name="pixels"/> the picture at its best.
    /// </summary>
    public void Register(PixelBuffer cells, PixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(pixels);
        var columns = cells.Width;
        var rows = cells.Height / 2;
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        var top = new uint[columns * rows];
        var bottom = new uint[columns * rows];
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                top[(row * columns) + col] = Pack(cells.GetPixel(col, row * 2));
                bottom[(row * columns) + col] = Pack(cells.GetPixel(col, (row * 2) + 1));
            }
        }

        foreach (var existing in _pictures)
        {
            if (existing.Columns == columns && existing.Rows == rows && existing.Top.AsSpan().SequenceEqual(top) &&
                existing.Bottom.AsSpan().SequenceEqual(bottom))
            {
                return;
            }
        }

        var picture = new Picture(columns, rows, top, bottom, pixels);
        _pictures.AddFirst(picture);
        Index(picture);
        while (_pictures.Count > Capacity)
        {
            var oldest = _pictures.Last!.Value;
            _pictures.RemoveLast();
            Unindex(oldest);
        }
    }

    /// <summary>Forgets what has been sent, so every picture on the next frame is sent again.</summary>
    public void Invalidate() => _shown = new();

    /// <summary>
    /// Finds the pictures on this frame and returns what to write to the terminal once the frame is out:
    /// one string, or null when nothing new needs drawing.
    /// </summary>
    /// <param name="buffer">The frame, as the window painted it.</param>
    /// <param name="left">The window's column on screen.</param>
    /// <param name="top">The window's row on screen.</param>
    /// <param name="screenHeight">Rows on screen. A picture is never drawn on the last one, where a Sixel
    /// that ended would scroll the terminal.</param>
    public string? OnFrame(CharacterBuffer buffer, int left, int top, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (_pictures.Count == 0)
        {
            _shown.Clear();
            return null;
        }

        if (_screen != (buffer.Width, buffer.Height))
        {
            _screen = (buffer.Width, buffer.Height);
            _shown.Clear();
        }

        var placements = Locate(buffer);
        var next = new Dictionary<(Picture, int X, int Y, int FirstRow, int Rows), long>(placements.Count);
        StringBuilder? output = null;
        foreach (var p in placements)
        {
            var screenY = top + p.Y;
            var rows = Math.Min(p.Rows, screenHeight - 1 - screenY);
            if (rows <= 0)
            {
                continue;
            }

            var key = (p.Picture, p.X, p.Y, p.FirstRow, rows);
            var stamp = RowsStamp(buffer, p.Y, rows);
            next[key] = stamp;
            if (_shown.TryGetValue(key, out var was) && was == stamp)
            {
                continue;
            }

            output ??= new StringBuilder();
            output.Append("\u001b7")
                .Append("\u001b[").Append(screenY + 1).Append(';').Append(left + p.X + 1).Append('H')
                .Append(p.Picture.Sixel(p.FirstRow, rows, _cellWidth, _cellHeight))
                .Append("\u001b8");
        }

        _shown = next;
        return output?.ToString();
    }

    /// <summary>Every run of a remembered picture's rows on this frame, top row first.</summary>
    internal List<Placement> Locate(CharacterBuffer buffer)
    {
        var found = new Dictionary<(int X, int Y), (Picture Picture, int Row)>();
        for (var y = 0; y < buffer.Height; y++)
        {
            for (var x = 0; x < buffer.Width; x++)
            {
                var cell = buffer.GetCell(x, y);
                if (cell.Character.Value != UpperHalf ||
                    !_byFirstCell.TryGetValue((Pack(cell.Foreground), Pack(cell.Background)), out var candidates))
                {
                    continue;
                }

                foreach (var (picture, row) in candidates)
                {
                    if (RowMatches(buffer, x, y, picture, row))
                    {
                        found[(x, y)] = (picture, row);
                        x += picture.Columns - 1;
                        break;
                    }
                }
            }
        }

        var placements = new List<Placement>();
        foreach (var ((x, y), (picture, row)) in found)
        {
            if (found.TryGetValue((x, y - 1), out var above) && above.Picture == picture && above.Row == row - 1)
            {
                continue; // not the top of its run
            }

            var count = 1;
            while (found.TryGetValue((x, y + count), out var below) && below.Picture == picture && below.Row == row + count)
            {
                count++;
            }

            placements.Add(new Placement(picture, x, y, row, count));
        }

        placements.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        return placements;
    }

    private static bool RowMatches(CharacterBuffer buffer, int x, int y, Picture picture, int row)
    {
        if (x + picture.Columns > buffer.Width)
        {
            return false;
        }

        var offset = row * picture.Columns;
        for (var col = 0; col < picture.Columns; col++)
        {
            var cell = buffer.GetCell(x + col, y);
            if (cell.Character.Value != UpperHalf ||
                Pack(cell.Foreground) != picture.Top[offset + col] ||
                Pack(cell.Background) != picture.Bottom[offset + col])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A fingerprint of whole rows, so a change anywhere on a row the picture sits on is noticed.</summary>
    private static long RowsStamp(CharacterBuffer buffer, int y, int rows)
    {
        var hash = new HashCode();
        for (var row = y; row < y + rows; row++)
        {
            for (var x = 0; x < buffer.Width; x++)
            {
                var cell = buffer.GetCell(x, row);
                hash.Add(cell.Character.Value);
                hash.Add(Pack(cell.Foreground));
                hash.Add(Pack(cell.Background));
            }
        }

        return hash.ToHashCode();
    }

    private void Index(Picture picture)
    {
        for (var row = 0; row < picture.Rows; row++)
        {
            var key = (picture.Top[row * picture.Columns], picture.Bottom[row * picture.Columns]);
            if (!_byFirstCell.TryGetValue(key, out var list))
            {
                _byFirstCell[key] = list = new();
            }

            list.Add((picture, row));
        }
    }

    private void Unindex(Picture picture)
    {
        foreach (var list in _byFirstCell.Values)
        {
            list.RemoveAll(entry => entry.Picture == picture);
        }

        foreach (var key in _byFirstCell.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList())
        {
            _byFirstCell.Remove(key);
        }
    }

    private static uint Pack(ImagePixel p) => ((uint)p.R << 16) | ((uint)p.G << 8) | p.B;

    private static uint Pack(SharpConsoleUI.Color c) => ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    /// <summary>A run of one picture's rows on the frame.</summary>
    internal sealed record Placement(Picture Picture, int X, int Y, int FirstRow, int Rows);

    /// <summary>A remembered picture: the colours its rows were painted in, and the pixels to send.</summary>
    internal sealed class Picture(int columns, int rows, uint[] top, uint[] bottom, PixelBuffer pixels)
    {
        private readonly Dictionary<(int FirstRow, int Rows, int CellWidth, int CellHeight), string> _sixels = new();
        private PixelBuffer? _scaled;

        public int Columns { get; } = columns;

        public int Rows { get; } = rows;

        public uint[] Top { get; } = top;

        public uint[] Bottom { get; } = bottom;

        /// <summary>Rows <paramref name="firstRow"/> onwards as Sixel, scaled to the terminal's cell size.</summary>
        public string Sixel(int firstRow, int rows, int cellWidth, int cellHeight)
        {
            var key = (firstRow, rows, cellWidth, cellHeight);
            if (_sixels.TryGetValue(key, out var known))
            {
                return known;
            }

            _scaled ??= pixels.Resize(Columns * cellWidth, Rows * cellHeight);
            var encoded = SixelEncoder.EncodeCompact(new Slice(_scaled, firstRow * cellHeight, rows * cellHeight));
            if (_sixels.Count >= 8)
            {
                _sixels.Clear();
            }

            _sixels[key] = encoded;
            return encoded;
        }
    }

    private sealed class Slice(PixelBuffer source, int top, int height) : IImageSource
    {
        public int Width => source.Width;

        public int Height => height;

        public Rgba32 GetPixel(int x, int y)
        {
            var p = source.GetPixel(x, top + y);
            return new Rgba32(p.R, p.G, p.B);
        }
    }
}
