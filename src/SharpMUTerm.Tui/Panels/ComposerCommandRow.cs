using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Core.Commands;

namespace SharpMUTerm.Tui.Panels;

/// <summary>
/// The composer's command field in cells, one row above the text: <c> command  +pose</c>. F1 and the docked panel both
/// draw it for a post (<see cref="ComposerModel.HasCommandField"/>); a MOO or script edit has none.
/// <para>
/// <b>The field is a one-row editor, and its keys are the composer's to route.</b> ⇥ and ⇧⇥ move between it and the
/// text, ⏎ in it moves to the text (a command is one line), and everything else is the editor's. Pasted line breaks
/// become spaces on the way out (<see cref="ComposeMessage.CleanCommand"/>), so whatever it holds is one line.
/// </para>
/// </summary>
internal sealed class ComposerCommandRow
{
    /// <summary>The label column's width: <c> command </c> plus a cell.</summary>
    internal const int LabelWidth = 10;

    private readonly MarkupControl _label;

    /// <param name="scheme">The settings screens' palette the composer is painted in.</param>
    /// <param name="plane">
    /// The row's plane under the label (a <see cref="ScreenPalette"/> role), or null for the pane's own: the docked panel
    /// sits on its pane's plane, as its header does.
    /// </param>
    internal ComposerCommandRow(ScreenTheme scheme, string? plane)
    {
        Field = new MultilineEditControl
        {
            WrapMode = WrapMode.NoWrap,
            ShowLineNumbers = false,
            IsEditing = true,
            ViewportHeight = 1,
            MaxLength = 200,
            HorizontalScrollbarVisibility = ScrollbarVisibility.Never,
            VerticalScrollbarVisibility = ScrollbarVisibility.Never,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            EscapeExitsEditMode = false,
            PlaceholderText = ComposerModel.CommandPlaceholder,
        };

        _label = new MarkupControl(new List<string>()) { HorizontalAlignment = HorizontalAlignment.Stretch };
        Paint(scheme, plane);

        var grid = Controls.Grid().WithAlignment(HorizontalAlignment.Stretch);
        grid.Rows(GridLength.Cells(1)).Columns(GridLength.Cells(LabelWidth), GridLength.Star(1));
        grid.Place(_label, 0, 0, 1, 1);
        grid.Place(Field, 0, 1, 1, 1);
        Control = grid.Build();
    }

    /// <summary>The one-row editor holding the command.</summary>
    internal MultilineEditControl Field { get; }

    /// <summary>What the composer lays out.</summary>
    internal IWindowControl Control { get; }

    /// <summary>The command as it will be sent: one line, trimmed.</summary>
    internal string Text => ComposeMessage.CleanCommand(Field.GetContent());

    /// <summary>Re-colours the row: the label on <paramref name="plane"/>, the field in the screens' field well.</summary>
    internal void Paint(ScreenTheme scheme, string? plane)
    {
        if (plane is null)
        {
            _label.SetContent(new List<string> { "[dim] command[/]" });
        }
        else
        {
            _label.SetContent(new List<string> { scheme.Paint($"[{ScreenPalette.Label}] command[/]", plane) });
            _label.BackgroundColor = scheme.Colour(plane);
        }

        Field.BackgroundColor = scheme.Colour(ScreenPalette.FieldBg);
        Field.FocusedBackgroundColor = scheme.Colour(ScreenPalette.CursorBg);
        Field.ForegroundColor = scheme.Colour(ScreenPalette.Value);
        Field.FocusedForegroundColor = scheme.Colour(ScreenPalette.Value);
        Field.SelectionBackgroundColor = scheme.Colour(ScreenPalette.MenuSelectedBg);
        Field.SelectionForegroundColor = scheme.Colour(ScreenPalette.Value);
    }

    /// <summary>Replaces what the field holds, as one line.</summary>
    internal void Set(string? command)
    {
        var text = ComposeMessage.CleanCommand(command);
        if (!string.Equals(Field.GetContent(), text, StringComparison.Ordinal))
        {
            Field.SetContent(text);
        }
    }
}
