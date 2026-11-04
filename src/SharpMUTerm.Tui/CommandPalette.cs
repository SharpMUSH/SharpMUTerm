using SharpMUTerm.Client.Palette;
using SharpMUTerm.Core.Commands;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace SharpMUTerm.Tui;

/// <summary>
/// The command surface (⌃P): a modal search box over a live-generated catalog, ordered and grouped by the
/// shared view-model (<see cref="CommandPaletteModel"/>, in Client, so the window's palette can list the same
/// rows) and drawn by <see cref="CommandSurfaceRenderer"/>. ↑↓ walk the entries, ⏎ dispatches the selected id
/// and records it among the recent ones, Esc closes. The catalog, the suggestions and dispatch are supplied by
/// the app so this stays a thin view over tested logic.
/// </summary>
internal sealed class CommandPalette
{
    private readonly ConsoleWindowSystem _system;
    private readonly Func<IReadOnlyList<CommandItem>> _catalog;
    private readonly Func<string?> _context;
    private readonly Action<string> _dispatch;
    private readonly RecentCommands _recent;
    private readonly Func<IReadOnlyList<string>> _suggested;

    private Window? _window;

    /// <summary>
    /// The settings screens' palette for the active theme, so this popup follows Light and Solarized as they do
    /// (<see cref="ScreenTheme"/>): its plane, frame and markup are resolved through it. Set by the app on a theme change.
    /// </summary>
    internal ScreenTheme Scheme { get; set; } = ScreenTheme.Dark;
    private PromptControl? _search;
    private MarkupControl? _results;
    private PaletteView _view = Empty;
    private int _selected;
    private int _contentWidth;

    public CommandPalette(
        ConsoleWindowSystem system,
        Func<IReadOnlyList<CommandItem>> catalog,
        Func<string?> context,
        Action<string> dispatch,
        RecentCommands recent,
        Func<IReadOnlyList<string>> suggested)
    {
        _system = system;
        _catalog = catalog;
        _context = context;
        _dispatch = dispatch;
        _recent = recent;
        _suggested = suggested;
    }

    private static PaletteView Empty { get; } = new([], [], 0);

    public bool IsOpen => _window is not null;

    /// <summary>What the surface is listing right now, for a test.</summary>
    internal PaletteView View => _view;

    /// <summary>The selected entry's index in <see cref="PaletteView.Entries"/>, for a test.</summary>
    internal int Selected => _selected;

    /// <summary>Types <paramref name="query"/> into the open surface, as if it had been keyed in.</summary>
    internal void SetQuery(string query)
    {
        if (_search is null)
        {
            return;
        }

        _search.SetInput(query);
        _selected = 0;
        Refresh(query);
    }

    /// <summary>Opens the surface if closed, closes it if open (bound to ⌃P).</summary>
    public void Toggle()
    {
        if (_window is not null)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    private void Open()
    {
        _selected = 0;
        var prompt = Controls.Prompt("›");
        if (!Scheme.IsIdentity)
        {
            // Off Dark the framework's own dark-grey well would be a dark island on the themed surface.
            prompt = prompt
                .WithInputBackgroundColor(Scheme.Colour(ScreenPalette.EditBg))
                .WithInputFocusedBackgroundColor(Scheme.Colour(ScreenPalette.EditBg))
                .WithInputForegroundColor(Scheme.Colour(ScreenPalette.Value))
                .WithInputFocusedForegroundColor(Scheme.Colour(ScreenPalette.Value));
        }

        _search = prompt.Build();
        _search.InputChanged += (_, query) => Refresh(query);
        _search.Entered += (_, _) => Commit();

        _results = new MarkupControl(new List<string>());

        // Size the surface to hug the full (unfiltered) catalog so nothing scrolls and there's no empty
        // gutter: fit height to the row count and width to the widest row, both capped to the desktop. A
        // narrower desktop elides titles and details, never keys (CommandSurfaceRenderer).
        var initial = Build(string.Empty);
        var desktop = _system.DesktopDimensions;
        var height = Math.Clamp(initial.Rows.Count + 1 + 3, 6, Math.Max(6, desktop.Height - 2));
        _contentWidth = Math.Clamp(
            Math.Max(CommandSurfaceRenderer.NaturalWidth(initial), HeaderWidth(initial)) + 1,
            32,
            Math.Max(32, desktop.Width - 6));
        var width = _contentWidth + 2; // + the 1-cell left/right border

        // A clean overlay: single border, but no window chrome — no minimize/maximize/close buttons
        // (HideTitleButtons) and no resize grip (Resizable(false)); Esc closes it. It floats on
        // ScreenPalette.MenuBg — the tone the settings screens already reserve for a list drawn *over*
        // the rows beneath it — so the surface lifts off the workspace panes rather than matching them.
        // Centered() *after* WithSize(): it reads the bounds set so far and falls back to 80x25, so
        // centring first placed the surface as if it were that size — which only became visible once
        // the catalog grew tall enough for the misplacement to push its bottom border off-screen.
        _window = new WindowBuilder(_system)
            .WithTitle("Command surface")
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBackgroundColor(Scheme.Colour(ScreenPalette.MenuBg))
            .HideTitleButtons()
            .Resizable(false)
            .WithSize(width, height)
            .Centered()
            .AddControl(_search)
            .AddControl(_results)
            .OnClosed((_, _) => Reset())
            .Build();
        Scheme.Frame(_window);

        _window.PreviewKeyPressed += OnKey;
        _system.AddWindow(_window);
        Refresh(string.Empty);
    }

    private PaletteView Build(string query) =>
        CommandPaletteModel.Build(query, _catalog(), _recent.Ids, _suggested());

    // The context line ("N of M   acting on X") is measured too, so a long character name is not cut off.
    private int HeaderWidth(PaletteView view) =>
        CommandSurfaceRenderer.MaxWidth(CommandSurfaceRenderer.Render(view, -1, _context()).Take(1).ToList());

    private void Refresh(string query)
    {
        _view = Build(query);
        _selected = _view.Entries.Count == 0 ? -1 : Math.Clamp(_selected, 0, _view.Entries.Count - 1);
        Paint();
    }

    private void Paint() =>
        _results?.SetContent(Scheme.Lines(CommandSurfaceRenderer.Render(_view, _selected, _context(), _contentWidth), ScreenPalette.MenuBg));

    private void Move(int delta)
    {
        var count = _view.Entries.Count;
        if (count == 0)
        {
            return;
        }

        _selected = ((_selected + delta) % count + count) % count;
        Paint();
    }

    /// <summary>Selects the listed entry with <paramref name="id"/> and commits it, as arrows and ⏎ would; for a test.</summary>
    internal bool Choose(string id)
    {
        var index = _view.Entries.ToList().FindIndex(e => e.Item.Id == id);
        if (index < 0)
        {
            return false;
        }

        _selected = index;
        Commit();
        return true;
    }

    private void Commit()
    {
        if (_selected >= 0 && _selected < _view.Entries.Count)
        {
            var id = _view.Entries[_selected].Item.Id;
            Close();
            _recent.Record(id);
            _dispatch(id);
        }
    }

    private void OnKey(object? sender, KeyPressedEventArgs e)
    {
        switch (e.KeyInfo.Key)
        {
            case ConsoleKey.DownArrow:
                Move(1);
                e.Handled = true;
                break;
            case ConsoleKey.UpArrow:
                Move(-1);
                e.Handled = true;
                break;
            case ConsoleKey.Enter:
                Commit();
                e.Handled = true;
                break;
            case ConsoleKey.Escape:
                Close();
                e.Handled = true;
                break;
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
        _search = null;
        _results = null;
        _view = Empty;
    }
}
