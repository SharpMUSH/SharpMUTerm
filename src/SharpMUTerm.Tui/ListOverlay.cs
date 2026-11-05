using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpMUTerm.Core.Packages;

namespace SharpMUTerm.Tui;

/// <summary>
/// A modal list surface driven by callbacks: the package review and Packages screen (F10), the package
/// index, and the terminal's chooser for an MXP <c>&lt;SEND&gt;</c> menu. What it says is the caller's
/// render; what a key does is the caller's key handler, which says whether the surface stays open.
/// </summary>
internal sealed class ListOverlay
{
    private readonly ConsoleWindowSystem _system;

    private Window? _window;
    private MarkupControl? _content;
    private MarkupControl? _footerControl;
    private ScrollablePanelControl? _body;
    private Func<int, List<string>>? _render;
    private Func<int, List<string>>? _footer;
    private bool _scrollKeys;
    private Func<ConsoleKeyInfo, bool>? _key;
    private Func<bool>? _escape;
    private Action? _closed;
    private Action<string>? _link;
    private string _plane = ScreenPalette.MenuBg;
    private int _inset = 4;

    /// <summary>
    /// The columns the body gives up to the scroll panel's bar when keys are pinned (<c>footer:</c>): rendered at the full
    /// inner width, every line that filled it wrapped (the history view at 80×24 drew each line twice).
    /// </summary>
    private int _barColumns;

    private ScreenTheme _scheme;

    /// <summary>The palette it is painted in; the app replaces it when the theme changes, and the next time it is drawn follows.</summary>
    internal ScreenTheme Scheme { get => _scheme; set => _scheme = value ?? ScreenTheme.Dark; }

    public ListOverlay(ConsoleWindowSystem system, ScreenTheme? scheme = null)
    {
        _system = system;
        _scheme = scheme ?? ScreenTheme.Dark;
    }

    public bool IsOpen => _window is not null;

    /// <summary>Which surface is up: <c>review</c>, <c>list</c> or null.</summary>
    public string? Mode { get; private set; }

    /// <summary>What the surface shows now, for a test to read.</summary>
    internal IReadOnlyList<string> Lines =>
        _render is null ? [] : [.. _render(Width()), .. _footer?.Invoke(Width()) ?? []];

    /// <summary>The pinned footer's lines alone (empty for a surface with none), for a test.</summary>
    internal IReadOnlyList<string> FooterLines => _footer?.Invoke(Width()) ?? [];

    /// <summary>The body's scroll offset in rows (0 for a surface with no pinned footer), for a test.</summary>
    internal int BodyScroll => _body?.VerticalScrollOffset ?? 0;

    /// <summary>
    /// Opens a surface. <paramref name="render"/> draws it at a width; <paramref name="key"/> answers a key
    /// and returns whether the surface stays open. <paramref name="escape"/>, when given, is asked first about
    /// Esc and keeps the surface open by returning true — a surface with a step to go back from.
    /// </summary>
    /// <param name="escape">
    /// Asked first when Esc is pressed: true when the surface took it (to leave a sub-mode of its own — a name being
    /// typed, a delete awaiting its answer) and stays open; with none, or false, Esc closes.
    /// </param>
    /// <param name="closed">Told once when this surface closes, however it does (not when a later one replaces it).</param>
    /// <param name="footer">
    /// Rows pinned to the modal's bottom edge, outside the scrolling body: a consent screen's keys and the question that
    /// goes with them. A body taller than the screen scrolls under them, so the way to accept or decline is never
    /// scrolled away (the import review at 80×24 lost both). Each row is one line, never wrapped: fit it to the width.
    /// </param>
    /// <param name="scrollKeys">Whether ↑↓ PgUp PgDn Home End scroll the body before the key handler sees them.</param>
    /// <param name="link">
    /// Told the target of a <c>[link=…]</c> clicked in the body: this surface's own handler, never the output panes'
    /// (whose links are scheme-tagged server text). With none, a link in the body does nothing.
    /// </param>
    /// <param name="plane">The role the modal is painted on: a popup's <see cref="ScreenPalette.MenuBg"/> by default.</param>
    /// <param name="flush">
    /// No cell between the frame and the words, and the lines drawn at the whole inner width: for a surface that lays out
    /// its own heading and key bands edge to edge and indents its body itself (the welcome).
    /// </param>
    public void Open(
        string mode,
        string title,
        Func<int, List<string>> render,
        Func<ConsoleKeyInfo, bool> key,
        int? width = null,
        Func<bool>? escape = null,
        Action? closed = null,
        Func<int, List<string>>? footer = null,
        bool scrollKeys = false,
        Action<string>? link = null,
        string plane = ScreenPalette.MenuBg,
        bool flush = false)
    {
        if (_window is not null)
        {
            Close();
        }

        _link = link;
        _render = render;
        _key = key;
        _escape = escape;
        _closed = closed;
        _footer = footer;
        _scrollKeys = scrollKeys && footer is not null;
        _plane = plane;
        _inset = flush ? 2 : 4;
        _barColumns = footer is null ? 0 : 1;
        var desktop = _system.DesktopDimensions;
        var widest = Math.Clamp(desktop.Width - 8, 44, 110);
        var outer = width is { } asked ? Math.Clamp(asked + 4, 24, widest) : widest;
        var lines = render(outer - _inset - _barColumns);
        var footerLines = footer?.Invoke(outer - _inset) ?? [];
        // A modal whose body scrolls under pinned keys is at most four rows short of the desktop, so centred it leaves the
        // header, the command line and the status row in view (two short, the import review at 80×24 sat on the command
        // line). One without scrolling keeps the two: its last rows are its keys, and they must not be cut off.
        var height = Math.Clamp(Rows(lines, outer - (flush ? 2 : 3)) + footerLines.Count + 2, 8, Math.Max(8, desktop.Height - (footer is null ? 2 : 4)));
        _content = new MarkupControl(_scheme.Lines(lines, plane));
        _content.LinkClicked += (_, e) => _link?.Invoke(e.Url);
        IWindowControl body = _content;
        if (footer is not null)
        {
            _content.HorizontalAlignment = HorizontalAlignment.Stretch;
            _body = new ScrollablePanelControl
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Fill,
                VerticalScrollMode = ScrollMode.Scroll,
                HorizontalScrollMode = ScrollMode.None,
                ShowScrollbar = true,
                AutoScroll = false,
            };
            _body.AddControl(_content);
            body = _body;
            _footerControl = new MarkupControl(_scheme.Lines(footerLines, plane))
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                StickyPosition = StickyPosition.Bottom,
            };
        }

        // Centered() after WithSize(), for CommandPalette's reason.
        Window? built = null;
        built = new WindowBuilder(_system)
            .WithTitle(title)
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBackgroundColor(_scheme.Colour(plane))
            .HideTitleButtons()
            .Resizable(false)
            .WithPadding(new SharpConsoleUI.Layout.Padding(flush ? 0 : 1, 0, 0, 0)) // a cell between the frame and the words
            .WithSize(outer, height)
            .Centered()
            .AddControl(body)
            .OnClosed((_, _) =>
            {
                // A window closed because the next surface replaced it must not clear the next one's state.
                if (_window == built)
                {
                    var closed = _closed;
                    Reset();
                    closed?.Invoke();
                }
            })
            .Build();
        _window = built;
        _scheme.Frame(_window);
        if (flush)
        {
            // Its bands mark its top and foot, so the frame steps back to a rule (the mockup's).
            _window.ActiveBorderForegroundColor = _window.InactiveBorderForegroundColor = _scheme.Colour(ScreenPalette.Rule);
        }

        if (_footerControl is not null)
        {
            _window.AddControl(_footerControl);
        }

        _window.PreviewKeyPressed += OnKey;
        _system.AddWindow(_window);

        // Last, so a test (or anything else) that waits on the mode finds the window already up.
        Mode = mode;
    }

    /// <summary>
    /// The rows <paramref name="lines"/> take at <paramref name="width"/>, wrapped the way the window's control will wrap
    /// them — measured by the framework, never counted. A line wider than the window (F10's key line at 80 columns) is
    /// two rows, and sizing the modal by the line count left the last row of F10's detail under its bottom edge. One
    /// column narrower than the content, so a scroll bar's column cannot add a row the measure missed.
    /// </summary>
    internal static int Rows(IReadOnlyList<string> lines, int width)
    {
        if (lines.Count == 0)
        {
            return 0;
        }

        var probe = new MarkupControl(lines.ToList());
        width = Math.Max(1, width);
        return Math.Max(lines.Count, probe.MeasureDOM(new LayoutConstraints(width, width, 0, int.MaxValue)).Height);
    }

    public void Repaint()
    {
        if (_render is not null && _content is not null)
        {
            _content.SetContent(_scheme.Lines(_render(Width() - _barColumns), _plane));
            if (_footer is not null)
            {
                _footerControl?.SetContent(_scheme.Lines(_footer(Width()), _plane));
            }

            _window?.Invalidate(redrawAll: true);
        }
    }

    internal void SimulateKey(ConsoleKeyInfo key) => OnKey(this, new KeyPressedEventArgs(key, false));

    /// <summary>A click on a body link with target <paramref name="url"/>, for a test (the surface's own handler, as a click).</summary>
    internal void SimulateLink(string url)
    {
        if (_window is not null)
        {
            _link?.Invoke(url);
        }
    }

    public void Close()
    {
        if (_window is { } window)
        {
            _system.CloseModalWindow(window);
        }
    }

    private int Width() => Math.Max(20, (_window?.Width ?? 80) - _inset);

    private void OnKey(object? sender, KeyPressedEventArgs e)
    {
        if (_window is null || _key is null)
        {
            return;
        }

        e.Handled = true;

        // A key can start a fetch whose answer opens the next surface (browse, then a review). Only the
        // surface the key was pressed on is closed, so that answer is never closed with it.
        var window = _window;
        if (e.KeyInfo.Key == ConsoleKey.Escape && _escape?.Invoke() == true)
        {
            if (_window == window)
            {
                Repaint();
            }

            return;
        }

        if (_scrollKeys && _body is { } body && e.KeyInfo.Modifiers == 0 && ScrollBy(body, e.KeyInfo.Key) is { } rows)
        {
            if (rows == int.MinValue)
            {
                body.ScrollToTop();
            }
            else if (rows == int.MaxValue)
            {
                body.ScrollToBottom();
            }
            else
            {
                body.ScrollVerticalBy(rows);
            }

            _window.Invalidate(redrawAll: true);
            return;
        }

        if (e.KeyInfo.Key == ConsoleKey.Escape || !_key(e.KeyInfo))
        {
            if (_window == window)
            {
                Close();
            }

            return;
        }

        Repaint();
    }

    /// <summary>The rows a scroll key moves the body: a line, a page less one, or either end (<see cref="int.MinValue"/>, <see cref="int.MaxValue"/>).</summary>
    private static int? ScrollBy(ScrollablePanelControl body, ConsoleKey key)
    {
        var page = Math.Max(1, body.ActualHeight - 1);
        return key switch
        {
            ConsoleKey.UpArrow => -1,
            ConsoleKey.DownArrow => 1,
            ConsoleKey.PageUp => -page,
            ConsoleKey.PageDown => page,
            ConsoleKey.Home => int.MinValue,
            ConsoleKey.End => int.MaxValue,
            _ => null,
        };
    }

    private void Reset()
    {
        _window = null;
        _content = null;
        _footerControl = null;
        _body = null;
        _footer = null;
        _scrollKeys = false;
        _link = null;
        _render = null;
        _key = null;
        _escape = null;
        _closed = null;
        Mode = null;
    }
}
