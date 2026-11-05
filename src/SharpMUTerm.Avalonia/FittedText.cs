using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// A line of text with shorter forms, drawn in the longest that fits the width it is given — the client's own words
/// are never cut to an ellipsis when a shorter wording says the same (the cells' <c>KeyLadder</c> rule). The last form
/// is still trimmed if even it does not fit. The window's text is laid out in logical pixels, so a column that fits at
/// one display scale fits at all of them; what moves the width is the window's size, which at 125% and 150% on a
/// 1080p screen is smaller than the 1440 px the mockups were drawn at.
/// </summary>
internal sealed class FittedText : TextBlock
{
    private IReadOnlyList<string> _forms = [];

    public FittedText()
    {
        TextTrimming = TextTrimming.CharacterEllipsis;
    }

    /// <summary>The wordings, longest first; setting them shows the first until the next measure picks one.</summary>
    public IReadOnlyList<string> Forms
    {
        get => _forms;
        set
        {
            _forms = value;
            Text = value.Count > 0 ? value[0] : string.Empty;
            InvalidateMeasure();
        }
    }

    /// <summary>The form <paramref name="width"/> logical pixels hold: the first that fits, else the last.</summary>
    internal string Pick(double width)
    {
        if (_forms.Count == 0)
        {
            return string.Empty;
        }

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight);
        foreach (var form in _forms)
        {
            var measured = new FormattedText(form, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, null);
            if (measured.WidthIncludingTrailingWhitespace <= width + 0.01)
            {
                return form;
            }
        }

        return _forms[^1];
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_forms.Count > 1 && !double.IsInfinity(availableSize.Width))
        {
            var room = availableSize.Width - Padding.Left - Padding.Right;
            var pick = Pick(room);
            if (!string.Equals(pick, Text, StringComparison.Ordinal))
            {
                Text = pick;
            }
        }

        return base.MeasureOverride(availableSize);
    }
}
