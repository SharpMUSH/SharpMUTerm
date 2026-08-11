using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
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
    /// The narrowest the surface goes. Set by the footer: a surface too narrow for its own key hints
    /// would wrap them onto a second row and push the list up. Checked by test against
    /// <see cref="SearchPrompt.Hints"/> rather than trusted.
    /// </summary>
    internal const int MinimumWidth = 78;

    /// <summary>
    /// The rows the surface spends on something other than results: the query line, the count line, the
    /// blank above the footer, and the footer. Keep in step with <see cref="SearchPrompt.Render"/>.
    /// </summary>
    private const int ChromeRows = 4;

    private readonly ConsoleWindowSystem _system;
    private readonly Func<bool, IReadOnlyList<SearchCorpus>> _corpus;
    private readonly Action<SearchRow, string, int, int> _go;

    private Window? _window;
    private MarkupControl? _body;
    private IReadOnlyList<SearchRow> _rows = Array.Empty<SearchRow>();
    private string _query = string.Empty;
    private string? _error;
    private string _scope = string.Empty;
    private bool _regex;
    private bool _all;
    private int _held;
    private int _selected = -1;
    private int _contentWidth;
    private int _listRows;
    private int _first;

    /// <summary>
    /// <paramref name="corpus"/> is asked for the windows to search — all of them, or just the focused
    /// one — and is read afresh on every keystroke. <paramref name="go"/> is handed the chosen row, the
    /// query, and its ordinal out of the total, which is what the bar above the landed line says.
    /// </summary>
    public SearchSurface(
        ConsoleWindowSystem system,
        Func<bool, IReadOnlyList<SearchCorpus>> corpus,
        Action<SearchRow, string, int, int> go)
    {
        _system = system;
        _corpus = corpus;
        _go = go;
    }

    public bool IsOpen => _window is not null;

    /// <summary>What the surface is currently showing, for a headless test to read back.</summary>
    internal IReadOnlyList<string> Lines => SearchPrompt.Render(
        _rows, _query, _error, _regex, _all, _scope, _held, _selected, _contentWidth - 1, _listRows, _first);

    /// <summary>The query as typed so far.</summary>
    internal string Query => _query;

    /// <summary>The rows currently listed, in buffer order — what ↑↓ walk and ⏎ picks from.</summary>
    internal IReadOnlyList<SearchRow> Rows => _rows;

    /// <summary>Whether the query is being read as a pattern.</summary>
    internal bool Regex => _regex;

    /// <summary>Whether every window is being searched rather than the focused one.</summary>
    internal bool AllWindows => _all;

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
        _query = string.Empty;
        _error = null;
        _regex = false;
        _all = false;
        _selected = -1;
        _first = 0;
        Refilter(_query, -1);

        var desktop = _system.DesktopDimensions;

        // Sized once and never again, HistorySurface's rule: narrowing must pad the list area rather than
        // shrink the window, so the rows and the footer stay where the eye left them. There is no
        // unfiltered list to size to here — an empty query matches nothing — so the height is the room
        // there is rather than the room the results need.
        _listRows = Math.Max(3, desktop.Height - ChromeRows - 6);
        _contentWidth = Math.Clamp(
            SearchPrompt.MaxWidth(Lines) + 2, MinimumWidth, Math.Max(MinimumWidth, desktop.Width - 6));

        var width = _contentWidth + 2; // + the 1-cell left/right border
        var height = Math.Min(_listRows + ChromeRows + 2, Math.Max(ChromeRows + 3, desktop.Height - 2));

        _body = new MarkupControl(new List<string>());

        // Centred *after* WithSize, because the builder reads the bounds set so far and falls back to 80x25.
        _window = new WindowBuilder(_system)
            .WithTitle("Search output")
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBackgroundColor(new Color(ScreenPalette.MenuBg))
            .HideTitleButtons()
            .Resizable(false)
            .WithSize(width, height)
            .Centered()
            .AddControl(_body)
            .OnClosed((_, _) => Reset())
            .Build();

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

        var decision = SearchPrompt.Interpret(e.KeyInfo, _query, _selected, _rows.Count, _regex, _all);
        e.Handled = true;

        switch (decision.Action)
        {
            case SearchAction.Go:
                // Closed first: the reader is about to be looking at the pane this row is in, and a modal
                // still painted over it would hide the thing they asked to see.
                var row = _rows[_selected];
                var ordinal = _selected + 1;
                var total = _rows.Count;
                var query = _query;
                Close();
                _go(row, query, ordinal, total);
                break;

            case SearchAction.Cancel:
                Close();
                break;

            case SearchAction.Redraw:
                _regex = decision.Regex;
                _all = decision.AllWindows;
                Refilter(decision.Query, decision.Selected);
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
    private void Refilter(string query, int selected)
    {
        _query = query;

        var windows = _corpus(_all);
        _scope = windows.Count == 1 ? windows[0].Label : "the focused window";
        _held = windows.Sum(w => w.Lines.Count);

        var rows = new List<SearchRow>();
        string? error = null;
        foreach (var window in windows)
        {
            var result = OutputSearch.Match(window.Lines, query, _regex);
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

        _error = error;
        _rows = rows;
        _selected = _rows.Count == 0 ? -1 : Math.Clamp(selected, 0, _rows.Count - 1);
        _first = SearchPrompt.Scroll(_first, _selected, _rows.Count, _listRows);
    }

    private void Paint() => _body?.SetContent(new List<string>(Lines));

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
    }
}
