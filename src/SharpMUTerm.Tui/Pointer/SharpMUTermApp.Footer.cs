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
/// <b>The status row</b> is the markup <see cref="PaintStatus"/> writes, parsed by the framework's own parser into runs
/// (<see cref="StatusModel"/>) — so the footer says exactly what the cells would, notices, mode keys, meters, reconnect,
/// <c>● REC</c>, <c>♪ muted</c> and the scrollback segment included — and the row's cell is left out of the grid. A click
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

    /// <summary>
    /// The row's markup as runs: the framework's parse of exactly what the cells are given, one run per change of ink
    /// or weight, split into the left and right clusters, with the palette hint and a showing notice's action marked
    /// clickable.
    /// </summary>
    internal StatusModel BuildStatusModel()
    {
        var plane = WorkspacePalette.Backdrop(_theme);
        var fg = ToColor(_theme.StatusForeground);
        var bg = ToColor(plane);
        var runs = new List<StatusRun>();
        var text = new System.Text.StringBuilder();
        (string Ink, bool Bold)? style = null;
        foreach (var cell in MarkupParser.Parse(_statusBar.Text, fg, bg))
        {
            if (cell.IsWideContinuation)
            {
                continue;
            }

            var next = (ColourHex(cell.Foreground), cell.Decorations.HasFlag(TextDecoration.Bold));
            if (style is { } current && current != next)
            {
                runs.Add(new StatusRun(text.ToString(), current.Ink, current.Bold));
                text.Clear();
            }

            style = next;
            text.Append(cell.Character.ToString());
        }

        if (style is { } last && text.Length > 0)
        {
            runs.Add(new StatusRun(text.ToString(), last.Ink, last.Bold));
        }

        IReadOnlyList<StatusRun> marked = runs;
        if (_noticeShown?.Action is { } action && _notice is not null)
        {
            foreach (var hint in NoticeActionHints(action))
            {
                marked = StatusModel.Mark(marked, hint, action.Command);
            }
        }

        marked = StatusModel.Mark(marked, PaletteHintText, HeaderModel.PaletteTarget);
        return StatusModel.From(marked, Hex(plane));
    }

    /// <summary>
    /// The model as cell markup: each run in its ink, bold where it was, the clusters a gap apart. The row the cells
    /// paint is the markup this model was parsed from; this is the way back, and <c>StatusFooterTests</c> holds the two
    /// to the same visible text.
    /// </summary>
    internal static string StatusRowMarkup(StatusModel model)
    {
        static string Run(StatusRun run) =>
            $"[{(run.Bold ? "bold " : string.Empty)}{run.Ink}]{MarkupText.Escape(run.Text)}[/]";

        var left = string.Concat(model.Left.Select(Run));
        return model.Right.Count == 0 ? left : left + "   " + string.Concat(model.Right.Select(Run));
    }

    /// <summary>The status row's way to the palette, as the row writes it.</summary>
    private static string PaletteHintText => $"{KeyGlyphs.Normalize("⌃P")} palette";

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
        if (top <= 0)
        {
            return null;
        }

        var prompts = new List<InputPrompt> { new(top, MarkupParser.Remove(_input.Prompt)) };
        if (_second.Visible)
        {
            prompts.Add(new InputPrompt(top + _input.ActualHeight, MarkupParser.Remove(_second.Prompt)));
        }

        return new InputBand(top, rows, InputLayout.HostFieldPad, prompts, $"⏎ sends · {KeyGlyphs.Normalize(Host.Keys.Newline.Glyph)} new line");
    }

    private static string ColourHex(Color colour) => $"#{colour.R:x2}{colour.G:x2}{colour.B:x2}";
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
    /// <inheritdoc/>
    public bool Equals(InputBand? other) =>
        other is not null && Top == other.Top && Rows == other.Rows && Pad == other.Pad && Hint == other.Hint
        && Prompts.SequenceEqual(other.Prompts);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Top, Rows, Pad, Hint);
}
