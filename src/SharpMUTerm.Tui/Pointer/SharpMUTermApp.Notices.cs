using System.Globalization;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Text;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// The notice surface (<see cref="SurfaceCatalog.Notice"/>): one <see cref="NoticeModel"/> per <see cref="Notice"/>,
/// drawn by the terminal on the status row (<see cref="NoticeMarkup"/>, unchanged from before the model existed) and
/// handed to a host that draws notices natively (<see cref="IPointerPresenter.Notice"/>) as the same instance. The
/// window's toast is an overlay: the status row's cells still carry the notice under it in every host, so the cells
/// of both hosts stay the same and the message log (⌃P ▸ Show client messages) stays the one history.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>How many notices this run has raised; each model's <see cref="NoticeModel.Sequence"/>.</summary>
    private long _noticeSequence;

    /// <summary>The action commands of the notices a host was handed, newest last, so a click can be checked against them.</summary>
    private readonly List<string> _noticeActionsOffered = [];

    /// <summary>The most notice actions remembered; a host offers a click only while its toast is up.</summary>
    private const int NoticeActionsKept = 16;

    /// <summary>The one callback every published notice carries, so a host compares overlays by value.</summary>
    private Action<string>? _noticeRun;

    /// <summary>
    /// The log's severity in the notice's words. The log's <c>Warning</c> is "the client refused or did less" — a
    /// refusal; its <c>Error</c> is "something failed" — the notice's warning.
    /// </summary>
    internal static NoticeSeverity NoticeSeverityOf(MessageSeverity severity) => severity switch
    {
        MessageSeverity.Info => NoticeSeverity.Info,
        MessageSeverity.Error => NoticeSeverity.Warning,
        _ => NoticeSeverity.Refusal,
    };

    /// <summary>
    /// The status row's markup for a notice: the key chip in the notice ink, then the text — in the warning red when
    /// something failed, otherwise a muted grey. Every ink is held to 4.5:1 on the backdrop the row is painted on: a
    /// failure in the raw red was 1.39:1 on Light, and the framework's fixed <c>[dim]</c> grey 2.4:1. The cell
    /// renderer of the notice surface.
    /// </summary>
    internal string NoticeMarkup(NoticeModel notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var plane = WorkspacePalette.Backdrop(_theme);
        var ink = notice.Severity == NoticeSeverity.Warning
            ? HeldOn(ScreenPalette.Warn, plane)
            : Hex(WorkspacePalette.NoticeMuted(_theme, plane));
        var body = $"[{ink}]{Escape(notice.Text)}[/]";
        var label = HeldOn(_ink.Notice, plane);
        // A source is a label (Sound:), a key a chord chip (⌃P): two shapes, so a word is never read as a key to press.
        var row = notice.Source is { } source ? $"[{label}]{Escape(source)}:[/] {body}"
            : notice.Key is null ? body : $"[{label}]{Escape(notice.Key)}[/] {body}";

        // The toast offers the action as a button; the row names the way to it, where it fits whole — never by cutting
        // the notice's own words, and never wrapping the row (FitStatusRow).
        // A notice whose own words already name the way (`offline — ⌥R (⌃P ▸ Reconnect) connects it`) gets no second
        // copy: the row read `… connects it · ⌥R Reconnect`.
        if (NoticeActionHints(notice.Action).FirstOrDefault(h => !NamesWay(notice.Text, h) && MarkupWidth(row) + 3 + h.Length <= HeaderWidth()) is { } hint)
        {
            row += $"[{label}] · {Escape(hint)}[/]";
        }

        return row;
    }

    /// <summary>
    /// The ways the status row may say a notice's action, longest first: its key and the action's name
    /// (<c>⌥R Reconnect</c>) when the command has a key on this host, else the ⌃P entry it is by title
    /// (<c>⌃P ▸ Show client messages</c>) and then by the action's shorter label, which ⌃P still finds (it matches word by
    /// word). None for a command ⌃P does not list (a saved scene's Open): that is the toast's alone, and the row will not
    /// name a way that is not there. Never a bare <c>⌃P</c>: a chip with no verb says nothing.
    /// </summary>
    internal IReadOnlyList<string> NoticeActionHints(NoticeAction? action)
    {
        if (action is null)
        {
            return [];
        }

        if (CommandKey(action.Command) is { } key)
        {
            return [$"{key} {action.Label}"];
        }

        if (BuildCatalog().FirstOrDefault(c => c.Id == action.Command) is not { } entry)
        {
            return [];
        }

        return string.Equals(entry.Title, action.Label, StringComparison.OrdinalIgnoreCase)
            ? [$"⌃P ▸ {entry.Title}"]
            : [$"⌃P ▸ {entry.Title}", $"⌃P ▸ {action.Label}"];
    }

    /// <summary>
    /// Whether <paramref name="text"/> already says <paramref name="hint"/>'s way to the action: its chord (<c>⌥R</c> of
    /// <c>⌥R Reconnect</c>), or the whole <c>⌃P ▸ …</c> path.
    /// </summary>
    internal static bool NamesWay(string text, string hint)
    {
        var way = hint.StartsWith("⌃P ▸ ", StringComparison.Ordinal) ? hint : hint.Split(' ')[0];
        return text.Contains(way, StringComparison.Ordinal);
    }

    /// <summary>A colour held to <see cref="WorkspacePalette.HeaderTextFloor"/> on <paramref name="plane"/>: notice text is small text.</summary>
    private static string HeldOn(string hex, Rgb plane) =>
        TryParseRgb(hex, out var rgb) ? Hex(Contrast.Legible(rgb, plane, WorkspacePalette.HeaderTextFloor)) : hex;

    /// <summary>Hands a notice to a host that draws notices natively; nothing for one that does not.</summary>
    private void PublishNotice(NoticeModel notice)
    {
        if (_pointer is null)
        {
            return;
        }

        foreach (var action in new[] { notice.Action, notice.Second })
        {
            if (action is null)
            {
                continue;
            }

            _noticeActionsOffered.Add(action.Command);
            if (_noticeActionsOffered.Count > NoticeActionsKept)
            {
                _noticeActionsOffered.RemoveAt(0);
            }
        }

        _noticeRun ??= id => OnUiThread(() => RunNoticeAction(id));
        _pointer.Notice(new NoticeOverlay(notice, NoticeAboveRow(), NoticePalette(), _noticeRun));
    }

    /// <summary>
    /// The row a toast sits above: the top of the command line and status row, which is the bottom of the pane area.
    /// A toast stacked above it covers neither the row it repeats nor the line being typed.
    /// </summary>
    private int NoticeAboveRow()
    {
        var bottom = 0;
        foreach (var rect in PaneOutputRects().Values)
        {
            bottom = Math.Max(bottom, rect.Y + rect.Height);
        }

        return bottom > 0 ? bottom : Math.Max(0, _system.ConsoleDriver.ScreenSize.Height - 2);
    }

    /// <summary>
    /// A notice's action, as a toast's click hands it back. Only a command a notice offered is run, through the
    /// registry as any other command is; anything else is refused, the chrome's rule. Internal for tests.
    /// </summary>
    internal bool RunNoticeAction(string command)
    {
        if (!_noticeActionsOffered.Contains(command, StringComparer.Ordinal))
        {
            RefuseCommand($"no notice offered {Snippet(command)}");
            return false;
        }

        return DispatchCommand(command);
    }

    /// <summary>
    /// The toast's colours: the status row's plane, the theme's text held to the floor on it, and one edge per
    /// severity — the accent for news, the notice voice for a refusal, the warning red for a failure, each held to a
    /// floor on the plane as well. An edge also inks the key chip and the action line, which are small text, so it is held to 4.5:1
    /// rather than the 3:1 floor.
    /// </summary>
    private NoticePalette NoticePalette()
    {
        var plane = _theme.StatusBackground;
        string On(string hex) => TryParseRgb(hex, out var rgb) ? Hex(Contrast.Legible(rgb, plane, 4.5)) : hex;
        return new NoticePalette(
            Hex(plane),
            Hex(Contrast.Legible(_theme.StatusForeground, plane, 4.5)),
            On(_ink.Accent),
            On(ScreenPalette.Warn),
            On(_ink.Notice));
    }

    private static bool TryParseRgb(string hex, out Rgb rgb)
    {
        rgb = default;
        if (hex.Length != 7 || hex[0] != '#'
            || !byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        rgb = new Rgb(r, g, b);
        return true;
    }

    /// <summary>The notice the status row's cells were last drawn from, or null; for a test comparing hosts.</summary>
    internal NoticeModel? LastNoticeModel => DrawnModel(SurfaceCatalog.Notice) as NoticeModel;

    /// <summary>Says a notice through the real <see cref="Notice"/>, as a test needs one with an action.</summary>
    internal void SimulateNotice(string text, MessageSeverity severity, string? key, NoticeAction? action, string? source = null) =>
        Notice(text, severity, key, action, source);
}

/// <summary>
/// A notice for a host that draws it natively, over the cells: the model the status row was drawn from, where the
/// pane area ends, the colours, and the one way back.
/// </summary>
/// <param name="Notice">The notice; the very instance the status row's cells were drawn from.</param>
/// <param name="AboveRow">The grid row a toast stacks above: the top of the command line and status row.</param>
/// <param name="Palette">The colours to draw it in, already held to the floor.</param>
/// <param name="Run">
/// Runs the notice's action by its command id, through the registry; a command no notice offered is refused. Safe to
/// call from any thread. The same instance for the life of the client.
/// </param>
internal sealed record NoticeOverlay(NoticeModel Notice, int AboveRow, NoticePalette Palette, Action<string> Run);

/// <summary>A toast's colours as <c>#rrggbb</c>: its plane, its text, and its edge per severity.</summary>
internal sealed record NoticePalette(string Plane, string Text, string Info, string Warning, string Refusal)
{
    /// <summary>The edge for <paramref name="severity"/>.</summary>
    public string Edge(NoticeSeverity severity) => severity switch
    {
        NoticeSeverity.Info => Info,
        NoticeSeverity.Warning => Warning,
        _ => Refusal,
    };
}
