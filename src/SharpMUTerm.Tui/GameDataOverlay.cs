using System.Globalization;
using SharpMUTerm.Core.Protocols;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace SharpMUTerm.Tui;

/// <summary>
/// The game data inspector (⌃P ▸ <c>Show game data</c>): every GMCP package and MSDP variable the focused
/// character's connection holds, how long ago each last changed, and the selected one's data in full.
/// ↑↓ select, Esc closes.
/// <para>
/// It exists because a meter or a script is written against a path, and the only way to know the path is to
/// see what the server sends. Mudlet's equivalent is a tree; a list and one pretty-printed payload answer the
/// same question in the space a terminal has.
/// </para>
/// </summary>
internal sealed class GameDataOverlay
{
    private readonly ConsoleWindowSystem _system;
    private readonly TimeProvider _time;

    private Window? _window;
    private MarkupControl? _content;
    private IReadOnlyList<GameDataEntry> _entries = [];
    private string _owner = string.Empty;
    private int _selected;

    public GameDataOverlay(ConsoleWindowSystem system, TimeProvider time)
    {
        _system = system;
        _time = time;
    }

    public bool IsOpen => _window is not null;

    /// <summary>Opens the inspector on <paramref name="data"/> if closed, closes it if open.</summary>
    public void Toggle(GameDataStore data, string owner)
    {
        if (_window is not null)
        {
            Close();
            return;
        }

        _entries = data.Entries;
        _owner = owner;
        _selected = 0;

        var desktop = _system.DesktopDimensions;
        var width = Math.Max(44, desktop.Width - 8);
        var height = Math.Max(10, desktop.Height - 4);
        _content = new MarkupControl(Lines(width - 3, height - 2));

        // Centered() after WithSize(), for CommandPalette's reason.
        _window = new WindowBuilder(_system)
            .WithTitle("Game data")
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBackgroundColor(new Color(ScreenPalette.MenuBg))
            .HideTitleButtons()
            .Resizable(false)
            .WithSize(width, height)
            .Centered()
            .AddControl(_content)
            .OnClosed((_, _) => Reset())
            .Build();

        _window.PreviewKeyPressed += OnKey;
        _system.AddWindow(_window);
    }

    /// <summary>
    /// The header, one row per package, a rule, and the selected package's data — as many of its lines as fit.
    /// Internal so the layout is testable without a window.
    /// </summary>
    internal List<string> Lines(int width, int height)
    {
        var lines = new List<string>
        {
            $"[{ScreenPalette.Accent}]{Escape(_owner)}[/] [{ScreenPalette.Label}]· {Count(_entries.Count)} · ↑↓ select · Esc close[/]",
            string.Empty,
        };

        if (_entries.Count == 0)
        {
            lines.Add($"[{ScreenPalette.Label}]nothing yet — this connection has received no GMCP or MSDP data.[/]");
            return lines;
        }

        // The list takes at most half the room, so the payload under it is always readable; it scrolls to
        // keep the selection in view.
        var listRows = Math.Clamp(_entries.Count, 1, Math.Max(3, (height - 4) / 2));
        var first = Math.Clamp(_selected - (listRows - 1), 0, Math.Max(0, _entries.Count - listRows));
        var nameWidth = Math.Min(32, _entries.Max(e => e.Package.Length));
        var now = _time.GetUtcNow();
        for (var i = first; i < first + listRows && i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var marker = i == _selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            var name = Elide(entry.Package, nameWidth).PadRight(nameWidth);
            var age = Age(now - entry.UpdatedAt).PadLeft(7);
            var preview = Elide(entry.Data?.ToJsonString() ?? "(no data)", Math.Max(4, width - nameWidth - 12));
            lines.Add($"{marker} [{(i == _selected ? "bold " : string.Empty)}{ScreenPalette.Value}]{Escape(name)}[/]" +
                $" [{ScreenPalette.Label}]{age}  {Escape(preview)}[/]");
        }

        lines.Add($"[{ScreenPalette.Label}]{new string('─', Math.Max(1, width))}[/]");
        var payload = GameDataStore.Pretty(_entries[_selected].Data).Split('\n');
        var room = Math.Max(1, height - lines.Count);
        foreach (var row in payload.Take(room))
        {
            lines.Add($"[{ScreenPalette.Value}]{Escape(Elide(row.TrimEnd('\r'), width))}[/]");
        }

        if (payload.Length > room)
        {
            lines[^1] = $"[{ScreenPalette.Label}]… {payload.Length - room + 1} more lines[/]";
        }

        return lines;
    }

    internal void Select(int delta)
    {
        if (_entries.Count == 0)
        {
            return;
        }

        _selected = Math.Clamp(_selected + delta, 0, _entries.Count - 1);
        Repaint();
    }

    private void Repaint()
    {
        if (_window is { } window)
        {
            _content?.SetContent(Lines(Math.Max(20, window.Width - 3), Math.Max(6, window.Height - 2)));
        }
    }

    private void OnKey(object? sender, KeyPressedEventArgs e)
    {
        switch (e.KeyInfo.Key)
        {
            case ConsoleKey.UpArrow:
                Select(-1);
                e.Handled = true;
                return;
            case ConsoleKey.DownArrow:
                Select(1);
                e.Handled = true;
                return;
            case ConsoleKey.PageUp:
                Select(-10);
                e.Handled = true;
                return;
            case ConsoleKey.PageDown:
                Select(10);
                e.Handled = true;
                return;
            case ConsoleKey.Escape:
                Close();
                e.Handled = true;
                return;
        }
    }

    private void Close()
    {
        if (_window is { } window)
        {
            _system.CloseModalWindow(window);
        }
    }

    private void Reset()
    {
        _window = null;
        _content = null;
        _entries = [];
    }

    private static string Count(int n) => n == 1 ? "1 package" : $"{n.ToString(CultureInfo.InvariantCulture)} packages";

    private static string Age(TimeSpan age) => age.TotalSeconds switch
    {
        < 1 => "now",
        < 60 => $"{(int)age.TotalSeconds}s ago",
        < 3600 => $"{(int)age.TotalMinutes}m ago",
        _ => $"{(int)age.TotalHours}h ago",
    };

    private static string Elide(string text, int width) =>
        text.Length <= width ? text : width <= 1 ? "…" : text[..(width - 1)] + "…";

    private static string Escape(string text) => MarkupText.Escape(text);
}
