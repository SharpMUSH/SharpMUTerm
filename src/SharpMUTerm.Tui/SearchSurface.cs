using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>One window the search can look in: what it is called, and the plain text it holds.</summary>
/// <param name="WindowId">The window's id — what <c>⏎</c> hands back so the app can activate it.</param>
/// <param name="Label">What to call it in the window column.</param>
/// <param name="Lines">Its lines' plain text, oldest first, indexed as the buffer indexes them.</param>
internal readonly record struct SearchCorpus(string WindowId, string Label, IReadOnlyList<string> Lines);

/// <summary>
/// The search surface (⌃F): a modal list of the lines matching a query, which typing narrows. It is the
/// host and nothing else — <see cref="SearchPrompt"/> owns what a keystroke means and what the surface
/// says, and <see cref="OutputSearch"/> owns the matching. The same split
/// <see cref="HistorySurface"/> uses, and the only reason any of it is testable without a terminal.
/// <para>
/// Keys arrive on <c>PreviewKeyPressed</c>, before any control sees them, as they do for the quit prompt,
/// the settings screens and the history surface. There is no framework input control here for the same
/// reason those have none: the query is a buffer this class owns and the whole body is markup redrawn on
/// every key, so there is nothing for focus to land on and nothing to keep in step.
/// </para>
/// <para>
/// <b>The corpus is read on every keystroke, not snapshotted at open.</b> A window's buffer is trimmed
/// from the front as it grows, so an index taken a minute ago points at a different line — and ⏎ hands an
/// index back. Re-reading is also what makes a search over a live connection show what is there now.
/// </para>
/// </summary>
internal sealed class SearchSurface
{
    /// <summary>
    /// The narrowest the surface goes. Set by the footer's shortest rung (<see cref="SearchPrompt.HintLadder"/>): a
    /// surface too narrow for it would wrap the keys onto a second row and push the list up.
    /// </summary>
    internal const int MinimumWidth = 44;

    /// <summary>
    /// The rows the surface spends on something other than results: the query line, the count line, the
    /// blank above the footer, and the footer. Keep in step with <see cref="SearchPrompt.Render"/>.
    /// </summary>
    private const int ChromeRows = 4;

    private readonly ConsoleWindowSystem _system;
    private readonly Func<string, bool, bool, bool, SearchModel> _search;
    private readonly Action<SearchModel, int> _go;
    private readonly Action? _closed;

    private Window? _window;

    /// <summary>
    /// The settings screens' palette for the active theme, so this popup follows Light and Solarized as they do
    /// (<see cref="ScreenTheme"/>): its plane, frame and markup are resolved through it. Set by the app on a theme change.
    /// </summary>
    internal ScreenTheme Scheme { get; set; } = ScreenTheme.Dark;
    private MarkupControl? _body;
    private SearchModel _model = Empty;
    private int _selected = -1;
    private int _contentWidth;
    private int _listRows;
    private int _first;

    /// <summary>A search that has not been typed yet.</summary>
    private static readonly SearchModel Empty = new(string.Empty, false, false, string.Empty, 0, null, []);

    /// <summary>
    /// <paramref name="search"/> runs the search for a query and the two toggles, reading the windows
    /// afresh — the client's <c>SearchModelFor</c>, which the window's native search calls too.
    /// <paramref name="go"/> is handed the model and the chosen row's index in it, which is everything the
    /// bar above the landed line says.
    /// </summary>
    public SearchSurface(
        ConsoleWindowSystem system,
        Func<string, bool, bool, bool, SearchModel> search,
        Action<SearchModel, int> go,
        Action? closed = null)
    {
        _system = system;
        _search = search;
        _go = go;
        _closed = closed;
    }

    /// <summary>
    /// The search for <paramref name="query"/> over <paramref name="windows"/>: every window's matches in
    /// buffer order, or none and the reason when the pattern does not parse. The one place a search is run,
    /// for the cell surface and the window's native one alike.
    /// </summary>
    internal static SearchModel Compute(IReadOnlyList<SearchCorpus> windows, string query, bool regex, bool all)
    {
        var scope = windows.Count == 1 ? windows[0].Label : "the focused window";
        var held = windows.Sum(w => w.Lines.Count);

        var rows = new List<SearchRow>();
        string? error = null;
        foreach (var window in windows)
        {
            var result = OutputSearch.Match(window.Lines, query, regex);
            if (result.Error is not null)
            {
                // One bad pattern is bad for every window, so it is reported once and the list is empty
                // — a result set holding whatever the earlier windows managed would be a search that
                // half-worked without saying which half.
                error = result.Error;
                rows.Clear();
                break;
            }

            rows.AddRange(result.Matches.Select(m =>
                new SearchRow(window.WindowId, window.Label, m.LineIndex, m.Text, m.MatchStart, m.MatchLength)));
        }

        return new SearchModel(query, regex, all, scope, held, error, rows);
    }

    public bool IsOpen => _window is not null;

    /// <summary>What the surface is currently showing, for a headless test to read back.</summary>
    internal IReadOnlyList<string> Lines => SearchPrompt.Render(_model, _selected, _contentWidth - 1, _listRows, _first);

    /// <summary>The model the surface is drawn from now, or null while it is closed.</summary>
    internal SearchModel? Model => _window is null ? null : _model;

    /// <summary>The query as typed so far.</summary>
    internal string Query => _model.Query;

    /// <summary>The rows currently listed, in buffer order — what ↑↓ walk and ⏎ picks from.</summary>
    internal IReadOnlyList<SearchRow> Rows => _model.Rows;

    /// <summary>Whether the query is being read as a pattern.</summary>
    internal bool Regex => _model.Regex;

    /// <summary>Whether every window is being searched rather than the focused one.</summary>
    internal bool AllWindows => _model.AllWindows;

    /// <summary>Whether older history is being searched too (⌥H).</summary>
    internal bool Older => _model.Older;

    /// <summary>
    /// Re-reads the model for the query and toggles as they stand and redraws, keeping the pointer: the older-history
    /// search calls it as its results stream in.
    /// </summary>
    internal void Refresh()
    {
        if (_window is null)
        {
            return;
        }

        Refilter(_model.Query, _model.Regex, _model.AllWindows, _model.Older, _selected);
        Paint();
        _window.Invalidate(redrawAll: true);
    }

    /// <summary>Opens the surface, or closes it when ⌃F arrives a second time.</summary>
    public void Toggle()
    {
        if (_window is not null)
        {
            Close();
            return;
        }

        Open();
    }

    /// <summary>Opens the surface into a headless frame (used by the <c>search</c> snapshot views).</summary>
    public void OpenForSnapshot() => Open();

    /// <summary>
    /// Feeds one key to the very handler <c>PreviewKeyPressed</c> raises, for the same reason
    /// <see cref="HistorySurface.SimulateKey"/> exists: the framework only pumps keys inside
    /// <c>Run()</c>, which a headless test or snapshot never enters.
    /// </summary>
    public void SimulateKey(ConsoleKeyInfo key) => OnKey(this, new KeyPressedEventArgs(key, false));

    /// <summary>Types a whole query in, one real keystroke at a time.</summary>
    public void SimulateTyping(string text)
    {
        foreach (var c in text)
        {
            SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }
    }

    private void Open()
    {
        _model = Empty;
        _selected = -1;
        _first = 0;
        Refilter(string.Empty, false, false, false, -1);

        var desktop = _system.DesktopDimensions;

        // Sized once and never again, HistorySurface's rule: narrowing must pad the list area rather than
        // shrink the window, so the rows and the footer stay where the eye left them. There is no
        // unfiltered list to size to here — an empty query matches nothing — so *both* dimensions are the
        // room there is rather than the room the results need. The height always was; the width was
        // measured against the only content an empty surface has, which is its own footer, so it opened
        // at eighty-odd cells on any terminal and every result was elided to fit a window sized by a key
        // hint. What the surface holds is a game's own lines, and they are wider than that by design.
        _listRows = Math.Max(3, desktop.Height - ChromeRows - 6);
        _contentWidth = Math.Max(MinimumWidth, desktop.Width - 6);

        var width = _contentWidth + 3; // + the 1-cell left/right border and the cell of padding on the left
        var height = Math.Min(_listRows + ChromeRows + 2, Math.Max(ChromeRows + 3, desktop.Height - 2));

        _body = new MarkupControl(new List<string>());

        // Centred *after* WithSize, because the builder reads the bounds set so far and falls back to 80x25.
        _window = new WindowBuilder(_system)
            .WithTitle("Search output")
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBackgroundColor(Scheme.Colour(ScreenPalette.MenuBg))
            .HideTitleButtons()
            .Resizable(false)
            .WithPadding(new SharpConsoleUI.Layout.Padding(1, 0, 0, 0)) // a cell between the frame and the words
            .WithSize(width, height)
            .Centered()
            .AddControl(_body)
            .OnClosed((_, _) => Reset())
            .Build();
        Scheme.Frame(_window);

        _window.PreviewKeyPressed += OnKey;
        _system.AddWindow(_window);
        Paint();
    }

    private void OnKey(object? sender, KeyPressedEventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        var decision = SearchPrompt.Interpret(e.KeyInfo, _model.Query, _selected, _model.Rows.Count, _model.Regex, _model.AllWindows, _model.Older);
        e.Handled = true;

        switch (decision.Action)
        {
            case SearchAction.Go:
                // Closed first: the reader is about to be looking at the pane this row is in, and a modal
                // still painted over it would hide the thing they asked to see.
                var model = _model;
                var index = _selected;
                Close();
                _go(model, index);
                break;

            case SearchAction.Cancel:
                Close();
                break;

            case SearchAction.Redraw:
                Refilter(decision.Query, decision.Regex, decision.AllWindows, decision.Older, decision.Selected);
                Paint();
                _window.Invalidate(redrawAll: true);
                break;

            case SearchAction.None:
            default:
                break;
        }
    }

    /// <summary>
    /// Re-reads the corpus and re-runs the search. The pointer is clamped to what the query actually
    /// matched: a narrowing must not leave ⏎ aimed past the end of the list.
    /// </summary>
    private void Refilter(string query, bool regex, bool all, bool older, int selected)
    {
        _model = _search(query, regex, all, older);
        _selected = _model.Rows.Count == 0 ? -1 : Math.Clamp(selected, 0, _model.Rows.Count - 1);
        _first = SearchPrompt.Scroll(_first, _selected, _model.Rows.Count, _listRows);
    }

    private void Paint() => _body?.SetContent(Scheme.Lines(Lines, ScreenPalette.MenuBg));

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
        _body = null;
        _closed?.Invoke();
    }
}
