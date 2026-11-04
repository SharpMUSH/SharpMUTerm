using SharpConsoleUI;
using SharpConsoleUI.Parsing;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Client.Surfaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// The status row and the command line's field, for a host that draws them itself (<see cref="HostChrome.Status"/>,
/// <see cref="HostChrome.Input"/>; the window).
/// <para>
/// <b>The status row</b> is a <see cref="StatusModel"/> built first — segments by role, laid out to the row's width — and
/// <see cref="PaintStatus"/> renders the cells from it (<see cref="StatusRowMarkup(StatusModel)"/>), so the footer says
/// exactly what the cells would, notices, mode keys, meters, reconnect, <c>● REC</c>, <c>♪ muted</c> and the scrollback
/// segment included — and the row's cell is left out of the grid. A click
/// hands back a target the client put on a run: a notice's action (to <see cref="RunNoticeAction"/>, the toast's path)
/// or the palette hint (to the palette, the header search box's path). Anything else is refused.
/// </para>
/// <para>
/// <b>The command line stays the cell editor.</b> Every key, the history, completion, selection and paste go where
/// they always went; the client only pads the bar with band-coloured columns (<see cref="InputLayout.HostFieldPad"/>)
/// and says, with every frame, which rows the bars are on (<see cref="InputBand"/>). The host draws the field around
/// those rows — its frame, its padding, the prompt in its UI face and the newline hint over blank cells — in its own
/// units. The band colour is read off the cells, so armed, idle and the character's tint read exactly as they do there.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private StatusOverlay? _statusOverlay;
    private Action<string>? _statusActivate;
    private InputBand? _inputBand;
    private bool _inputBandSaid;

    /// <summary>Tells a host that draws the status row what it says, when that changed.</summary>
    private void PublishStatus()
    {
        if (_pointer is null || !Host.Chrome.Status || _window is null)
        {
            return;
        }

        var overlay = BuildStatusOverlay();
        if (overlay == _statusOverlay)
        {
            return;
        }

        _statusOverlay = overlay;
        Drawing(SurfaceCatalog.Status, string.Empty, overlay.Model);
        _pointer.Status(overlay);
    }

    /// <summary>The status row as the host is to draw it. Internal for tests.</summary>
    internal StatusOverlay BuildStatusOverlay()
    {
        _statusActivate ??= target => OnUiThread(() => ActivateStatusTarget(target));
        return new StatusOverlay(BuildStatusModel(), MainWindowInFront(), _statusActivate);
    }

    /// <summary>The status row's model, as last painted: the instance the cells were rendered from. Internal for tests.</summary>
    internal StatusModel BuildStatusModel() => _statusModel ?? StatusRowModel([]);

    /// <summary>The model the status row was last rendered from.</summary>
    private StatusModel? _statusModel;

    /// <summary>A status row laid out at the row's width now, on the row's plane, in the row's ink.</summary>
    private StatusModel StatusRowModel(
        IReadOnlyList<StatusSegment> left,
        IReadOnlyList<StatusSegment>? right = null,
        NoticeModel? notice = null) =>
        new(left, right ?? [], Hex(WorkspacePalette.Backdrop(_theme)), Hex(_theme.StatusForeground))
        {
            Width = HeaderWidth(),
            Notice = notice,
        };

    /// <summary>
    /// The status row's quiet ink: a grey held to 4.5:1 on the backdrop it is painted on, in place of the framework's
    /// fixed <c>[dim]</c> <c>#808080</c> — 3.4:1 on Light's status plane, and the row is small text a reader looks for
    /// (UX audit wave 11, #2 and #7).
    /// </summary>
    private string StatusDim => Hex(WorkspacePalette.NoticeMuted(_theme, WorkspacePalette.Backdrop(_theme)));

    /// <summary>A run of the status row; a null ink is the row's own.</summary>
    private static StatusRun Run(string text, string? ink = null, bool bold = false, string? target = null) =>
        new(text, ink, bold, target);

    /// <summary>A run in the row's quiet ink.</summary>
    private StatusRun Dim(string text, string? target = null) => new(text, StatusDim, false, target);

    /// <summary>A segment of the status row.</summary>
    private static StatusSegment Segment(StatusRole role, params StatusRun[] runs) => new(role, runs);

    /// <summary>
    /// The cell renderer of the status row: each run in its ink, bold where it is, a run in the row's own ink written
    /// with no colour (so it takes the control's, as unstyled text always did), neighbouring runs of one style merged.
    /// </summary>
    internal static string StatusRowMarkup(IReadOnlyList<StatusRun> runs)
    {
        var markup = new System.Text.StringBuilder();
        for (var i = 0; i < runs.Count;)
        {
            var (ink, bold) = (runs[i].Ink, runs[i].Bold);
            var text = new System.Text.StringBuilder();
            for (; i < runs.Count && runs[i].Ink == ink && runs[i].Bold == bold; i++)
            {
                text.Append(runs[i].Text);
            }

            if (text.Length == 0)
            {
                continue;
            }

            var escaped = MarkupText.Escape(text.ToString());
            var tag = (bold ? "bold " : string.Empty) + (ink ?? string.Empty);
            markup.Append(tag.Length == 0 ? escaped : $"[{tag.TrimEnd()}]{escaped}[/]");
        }

        return markup.ToString();
    }

    /// <summary>The status row's way to the palette, as the row writes it; the run a click opens ⌃P from.</summary>
    private const string PaletteHintText = "⌃P palette";

    /// <summary>The model as cell markup, laid out at its width: what the cells paint. Internal for tests.</summary>
    internal static string StatusRowMarkup(StatusModel model) => StatusRowMarkup(model.Row());

    /// <summary>
    /// A click on the host's status footer, by the target the client put on a run. Internal so a test can drive it the
    /// way the host does; returns whether the target was offered and was acted on.
    /// </summary>
    internal bool ActivateStatusTarget(string target)
    {
        var offered = _statusOverlay ?? BuildStatusOverlay();
        if (!offered.Model.Targets().Contains(target, StringComparer.Ordinal))
        {
            RefuseCommand($"the status row never offered {Snippet(target)}");
            return false;
        }

        if (!MainWindowInFront())
        {
            return false;
        }

        if (target == HeaderModel.PaletteTarget)
        {
            if (!_palette.IsOpen && !NativePaletteIsOpen)
            {
                OpenMenu();
            }

            return true;
        }

        return RunNoticeAction(target);
    }

    /// <summary>What the host was last told about the status row. Internal for tests.</summary>
    internal StatusOverlay? StatusOverlayPublished => _statusOverlay;

    /// <summary>
    /// Tells a host that draws the command line's field which rows the bars are on, or that there is no field to draw
    /// (another of the client's windows is in front: its cells, not the bars, are at the bottom of the screen). Called
    /// on every paint of the main window, before the frame is flushed, so the host reads it with the frame it belongs to.
    /// </summary>
    private void PublishInputBand()
    {
        if (_pointer is null || !Host.Chrome.Input || _window is null)
        {
            return;
        }

        var band = InputBandNow();
        if (_inputBandSaid && band == _inputBand)
        {
            return;
        }

        (_inputBand, _inputBandSaid) = (band, true);
        _pointer.InputBand(band);
    }

    /// <summary>Where the bars are now, or null with no field to draw. Internal for tests.</summary>
    internal InputBand? InputBandNow()
    {
        if (!Host.Chrome.Input || !MainWindowInFront() || AnyOverlayOpen || _input.ActualHeight <= 0)
        {
            return null;
        }

        var rows = _input.ActualHeight + (_second.Visible ? _second.ActualHeight : 0);
        var top = _system.ConsoleDriver.ScreenSize.Height - rows - (_statusBar.Visible ? Math.Max(0, _statusBar.ActualHeight) : 0);
        int? left = null, columns = null;

        // In the output's column (HostChrome.InputInColumn) the bars are where they were arranged, not across the bottom.
        if (Host.Chrome.InputInColumn && BoundsOf(_input) is { } box)
        {
            (top, left, columns) = (box.Y, box.X, box.Width);
        }

        if (top <= 0)
        {
            return null;
        }

        var prompts = new List<InputPrompt> { new(top, MarkupParser.Remove(_input.Prompt)) };
        if (_second.Visible)
        {
            prompts.Add(new InputPrompt(top + _input.ActualHeight, MarkupParser.Remove(_second.Prompt)));
        }

        return new InputBand(top, rows, InputLayout.HostFieldPad, prompts, $"⏎ sends · {KeyGlyphs.Normalize(Host.Keys.Newline.Glyph)} new line")
        {
            Left = left,
            Columns = columns,
        };
    }

}

/// <summary>The status row, for a host that draws it (<see cref="HostChrome.Status"/>).</summary>
/// <param name="Model">What the row says.</param>
/// <param name="Interactive">Whether the main window is in front; while a modal is, the footer takes no click.</param>
/// <param name="Activate">Acts on a run's target; safe from any thread, and the same instance for the life of the app.</param>
internal sealed record StatusOverlay(StatusModel Model, bool Interactive, Action<string> Activate);

/// <summary>A command line's prompt, by the screen row its first line is on, as plain text.</summary>
internal sealed record InputPrompt(int Row, string Text);

/// <summary>
/// The command line's rows, for a host that draws its field (<see cref="HostChrome.Input"/>): the field's frame and
/// padding go around rows <paramref name="Top"/> to <paramref name="Top"/> + <paramref name="Rows"/>, the full width.
/// </summary>
/// <param name="Top">The first screen row of the first bar.</param>
/// <param name="Rows">How many rows the bars take between them.</param>
/// <param name="Pad">The band-coloured columns the client leaves at each end of a bar row, for the field's inner padding.</param>
/// <param name="Prompts">Each bar's prompt, drawn in the bar's first cells after <paramref name="Pad"/>.</param>
/// <param name="Hint">The newline hint, in the cells' glyphs, for the host to draw where the first row is blank.</param>
internal sealed record InputBand(int Top, int Rows, int Pad, IReadOnlyList<InputPrompt> Prompts, string Hint)
{
    /// <summary>
    /// The first column of the bars, when they are a row of the output's column (<see cref="HostChrome.InputInColumn"/>);
    /// null when they span the grid from column 0.
    /// </summary>
    public int? Left { get; init; }

    /// <summary>How many columns the bars span, or null for the whole grid's width.</summary>
    public int? Columns { get; init; }

    /// <summary>The first column of the bars.</summary>
    public int From => Left ?? 0;

    /// <summary>One past the bars' last column, in a grid <paramref name="width"/> wide.</summary>
    public int To(int width) => Columns is { } columns ? Math.Min(width, From + columns) : width;

    /// <summary>Whether column <paramref name="x"/> is under the bars: from <see cref="Top"/> down, the rows are theirs there.</summary>
    public bool Spans(int x, int width) => x >= From && x < To(width);

    /// <inheritdoc/>
    public bool Equals(InputBand? other) =>
        other is not null && Top == other.Top && Rows == other.Rows && Pad == other.Pad && Hint == other.Hint
        && Left == other.Left && Columns == other.Columns && Prompts.SequenceEqual(other.Prompts);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Top, Rows, Pad, Hint);
}
