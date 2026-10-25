using System.Text;
using SharpConsoleUI;
using SharpConsoleUI.Layout;

namespace SharpMUTerm.Avalonia;

/// <summary>One painted cell, as the framework handed it over.</summary>
/// <param name="Rune">The character. A wide character's second column is a continuation cell.</param>
/// <param name="Combiners">Combining marks drawn with the character — and, on a Kitty placeholder, its row and column.</param>
internal readonly record struct GridCell(
    Rune Rune,
    string? Combiners,
    Color Foreground,
    Color Background,
    TextDecoration Decorations,
    bool IsWideContinuation)
{
    public static GridCell Blank { get; } = new(new Rune(' '), null, Color.Default, Color.Default, TextDecoration.None, false);
}

/// <summary>Where the caret is and how it is drawn, as of the frame it was published with.</summary>
internal readonly record struct CaretState(int X, int Y, bool Visible, SharpConsoleUI.Core.CursorShape Shape);

/// <summary>
/// A finished frame: what <see cref="AvaloniaConsoleDriver.Flush"/> published for the window to paint. Immutable,
/// so the framework's render thread can go on writing the next frame while the UI thread draws this one.
/// </summary>
internal sealed class ScreenFrame(int width, int height, GridCell[] cells, CaretState caret)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public CaretState Caret { get; } = caret;

    public GridCell this[int x, int y] => cells[(y * Width) + x];

    public static ScreenFrame Empty(int width, int height) =>
        new(width, height, Enumerable.Repeat(GridCell.Blank, width * height).ToArray(), default);

    /// <summary>The row's text with continuation cells dropped — for tests and for reading a frame back.</summary>
    public string RowText(int y)
    {
        var text = new StringBuilder(Width);
        for (var x = 0; x < Width; x++)
        {
            var cell = this[x, y];
            if (!cell.IsWideContinuation)
            {
                text.Append(cell.Rune.ToString());
            }
        }

        return text.ToString();
    }
}

/// <summary>
/// The driver's back buffer: the cells the framework has written since the last flush. This is the whole of
/// what a terminal would have been told, held as cells rather than serialised to escapes, which is what lets
/// the window draw a Kitty placeholder as the picture it names.
/// <para>
/// Writes come from the framework's render thread and resizes from the UI thread, so every member takes the
/// one lock. The wide-character repairs mirror <c>ConsoleBuffer</c>'s: overwriting half of a wide pair blanks
/// the other half, or a stale half would be left on screen.
/// </para>
/// </summary>
internal sealed class CellGrid
{
    private readonly Lock _gate = new();
    private GridCell[] _cells;

    public CellGrid(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _cells = Blank(Width * Height);
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        lock (_gate)
        {
            if (width == Width && height == Height)
            {
                return;
            }

            var resized = Blank(width * height);
            for (var y = 0; y < Math.Min(height, Height); y++)
            {
                Array.Copy(_cells, y * Width, resized, y * width, Math.Min(width, Width));
            }

            _cells = resized;
            Width = width;
            Height = height;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Fill(_cells, GridCell.Blank);
        }
    }

    public void Set(int x, int y, GridCell cell)
    {
        lock (_gate)
        {
            if (!Inside(x, y))
            {
                return;
            }

            RepairEdges(x, y, 1);
            _cells[(y * Width) + x] = cell;
        }
    }

    public void Fill(int x, int y, int width, GridCell cell)
    {
        lock (_gate)
        {
            if (!Inside(x, y) || width <= 0)
            {
                return;
            }

            width = Math.Min(width, Width - x);
            RepairEdges(x, y, width);
            Array.Fill(_cells, cell, (y * Width) + x, width);
        }
    }

    /// <summary>Copies one row of a framework <see cref="CharacterBuffer"/>, combiners and decorations included.</summary>
    public void CopyFrom(int destX, int destY, CharacterBuffer source, int srcX, int srcY, int width, Color fallbackBackground)
    {
        lock (_gate)
        {
            if (!Inside(destX, destY) || width <= 0)
            {
                return;
            }

            width = Math.Min(width, Width - destX);
            RepairEdges(destX, destY, width);
            var row = destY * Width;
            for (var i = 0; i < width; i++)
            {
                var sx = srcX + i;
                if (sx < 0 || sx >= source.Width || srcY < 0 || srcY >= source.Height)
                {
                    _cells[row + destX + i] = GridCell.Blank with { Background = fallbackBackground };
                    continue;
                }

                var c = source.GetCell(sx, srcY);

                // A wide character cut by the copy's edges has lost a half, and a lone half draws nothing
                // useful: the same rule ConsoleBuffer applies, for the same reason.
                var orphan = c.IsWideContinuation && i == 0;
                var clipped = !c.IsWideContinuation && i == width - 1
                    && sx + 1 < source.Width && source.GetCell(sx + 1, srcY).IsWideContinuation;
                _cells[row + destX + i] = orphan || clipped
                    ? new GridCell(new Rune(' '), null, c.Foreground, c.Background, c.Decorations, false)
                    : new GridCell(c.Character, c.Combiners, c.Foreground, c.Background, c.Decorations, c.IsWideContinuation);
            }
        }
    }

    public ScreenFrame Snapshot(CaretState caret)
    {
        lock (_gate)
        {
            return new ScreenFrame(Width, Height, (GridCell[])_cells.Clone(), caret);
        }
    }

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    private void RepairEdges(int x, int y, int width)
    {
        var row = y * Width;
        if (x > 0 && _cells[row + x].IsWideContinuation)
        {
            _cells[row + x - 1] = _cells[row + x - 1] with { Rune = new Rune(' '), Combiners = null };
        }

        var right = x + width;
        if (right < Width && _cells[row + right].IsWideContinuation)
        {
            _cells[row + right] = _cells[row + right] with { Rune = new Rune(' '), Combiners = null, IsWideContinuation = false };
        }
    }

    private static GridCell[] Blank(int count)
    {
        var cells = new GridCell[count];
        Array.Fill(cells, GridCell.Blank);
        return cells;
    }
}
