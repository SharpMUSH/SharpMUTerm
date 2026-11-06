using System.Globalization;
using SharpMUTerm.Client.Modals;
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

    /// <summary>
    /// The settings screens' palette for the active theme, so this popup follows Light and Solarized as they do
    /// (<see cref="ScreenTheme"/>): its plane, frame and markup are resolved through it. Set by the app on a theme change.
    /// </summary>
    internal ScreenTheme Scheme { get; set; } = ScreenTheme.Dark;
    private MarkupControl? _content;
    private GameDataModel _model = new(string.Empty, []);
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

        Model = _model = GameDataModel.From(data.Entries, owner, _time.GetUtcNow());
        _selected = 0;

        var desktop = _system.DesktopDimensions;
        var width = Math.Max(44, desktop.Width - 8);
        var height = Math.Max(10, desktop.Height - 4);
        _content = new MarkupControl(Scheme.Lines(Lines(width - 4, height - 2), ScreenPalette.MenuBg));

        // Centered() after WithSize(), for CommandPalette's reason.
        _window = new WindowBuilder(_system)
            .WithTitle(GameDataModel.Title)
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBackgroundColor(Scheme.Colour(ScreenPalette.MenuBg))
            .HideTitleButtons()
            .Resizable(false)
            .WithPadding(new SharpConsoleUI.Layout.Padding(1, 0, 0, 0)) // a cell between the frame and the words
            .WithSize(width, height)
            .Centered()
            .AddControl(_content)
            .OnClosed((_, _) => Reset())
            .Build();
        Scheme.Frame(_window);

        _window.PreviewKeyPressed += OnKey;
        _system.AddWindow(_window);
    }

    /// <summary>
    /// The header, one row per package, a rule, and the selected package's data — as many of its lines as fit.
    /// Internal so the layout is testable without a window.
    /// </summary>
    /// <summary>The model the open inspector was drawn from, for the surface parity test; null while closed.</summary>
    internal GameDataModel? Model { get; private set; }

    internal List<string> Lines(int width, int height) => Lines(_model, _selected, width, height);

    /// <summary>The inspector's rows for <paramref name="model"/> with row <paramref name="selected"/> open under the list.</summary>
    internal static List<string> Lines(GameDataModel model, int selected, int width, int height)
    {
        var rows = model.Rows;
        var lines = new List<string>
        {
            $"[{ScreenPalette.Accent}]{Escape(model.Owner)}[/] [{ScreenPalette.Label}]· {model.Count} · {GameDataModel.Keys}[/]",
            string.Empty,
        };

        if (rows.Count == 0)
        {
            lines.Add($"[{ScreenPalette.Label}]{Escape(GameDataModel.Empty)}[/]");
            return lines;
        }

        // The list takes at most half the room, so the payload under it is always readable; it scrolls to
        // keep the selection in view.
        var listRows = Math.Clamp(rows.Count, 1, Math.Max(3, (height - 4) / 2));
        var first = Math.Clamp(selected - (listRows - 1), 0, Math.Max(0, rows.Count - listRows));
        var nameWidth = Math.Min(32, rows.Max(e => e.Package.Length));
        for (var i = first; i < first + listRows && i < rows.Count; i++)
        {
            var entry = rows[i];
            var marker = i == selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            var name = Elide(entry.Package, nameWidth).PadRight(nameWidth);
            var age = entry.Age.PadLeft(7);
            var preview = Elide(entry.Preview, Math.Max(4, width - nameWidth - 12));
            lines.Add($"{marker} [{(i == selected ? "bold " : string.Empty)}{ScreenPalette.Value}]{Escape(name)}[/]" +
                $" [{ScreenPalette.Label}]{age}  {Escape(preview)}[/]");
        }

        lines.Add($"[{ScreenPalette.Label}]{new string('─', Math.Max(1, width))}[/]");
        var payload = rows[selected].Payload.Split('\n');
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
        if (_model.Rows.Count == 0)
        {
            return;
        }

        _selected = Math.Clamp(_selected + delta, 0, _model.Rows.Count - 1);
        Repaint();
    }

    private void Repaint()
    {
        if (_window is { } window)
        {
            _content?.SetContent(Scheme.Lines(Lines(Math.Max(20, window.Width - 4), Math.Max(6, window.Height - 2)), ScreenPalette.MenuBg));
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
        _model = new(string.Empty, []);
        Model = null;
    }

    private static string Elide(string text, int width) => MarkupText.ElidePlain(text, width);

    private static string Escape(string text) => MarkupText.Escape(text);
}
