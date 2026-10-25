using System.Collections;
using System.Reflection;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Parsing;

namespace SharpMUTerm.Tui;

/// <summary>
/// The two hit tests the framework does for a click and does not offer for a hover: which link of a
/// <see cref="MarkupControl"/> is under a cell, and which tab of a <see cref="TabControl"/>. Both are read
/// out of the geometry the control's own last paint recorded, by reflection, because that geometry is
/// private (SharpConsoleUI 2.7.8). <see cref="SelectedText"/> reads the same paint cache the same way.
/// <para>
/// Anything unexpected answers "nothing here" rather than throwing: a renamed field costs the hover cue
/// and nothing else, and <c>FrameworkHitTestTests</c> fails on the release that renames it.
/// </para>
/// </summary>
internal static class FrameworkHitTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo? RowLinksField = typeof(MarkupControl).GetField("_cachedRowLinks", Private);
    private static readonly FieldInfo? RowPaintXField = typeof(MarkupControl).GetField("_cachedRowPaintX", Private);
    private static readonly FieldInfo? OriginYField = typeof(MarkupControl).GetField("_cachedOriginY", Private);
    private static readonly FieldInfo? LockField = typeof(MarkupControl).GetField("_selectionLock", Private);

    private static readonly MethodInfo? TabAtX =
        typeof(TabControl).GetMethod("GetTabIndexAtX", Private, null, [typeof(int)], null);

    /// <summary>Whether every private member these read is still where it was.</summary>
    internal static bool Available =>
        RowLinksField is not null && RowPaintXField is not null && OriginYField is not null && LockField is not null
        && TabAtX is not null;

    /// <summary>
    /// The link at a cell of <paramref name="control"/>, measured from the control's own top-left (what the
    /// framework hands <c>ProcessMouseEvent</c>), with the columns it covers on that row; or null.
    /// </summary>
    internal static (LinkSpan Span, int Row, int PaintX)? LinkAt(MarkupControl control, int x, int y)
    {
        if (RowLinksField is null || RowPaintXField is null || OriginYField is null || LockField?.GetValue(control) is not { } gate)
        {
            return null;
        }

        lock (gate)
        {
            if (RowLinksField.GetValue(control) is not IList rows || OriginYField.GetValue(control) is not int originY)
            {
                return null;
            }

            var row = y - originY;
            if (row < 0 || row >= rows.Count || rows[row] is not List<LinkSpan> spans)
            {
                return null;
            }

            var paintX = RowPaintXField.GetValue(control) is int[] xs && row < xs.Length ? xs[row] : 0;
            foreach (var span in spans)
            {
                if (span.Contains(x - paintX))
                {
                    return (span, row, paintX);
                }
            }

            return null;
        }
    }

    /// <summary>
    /// The index of the tab whose label is at column <paramref name="x"/> of the strip (control-relative,
    /// on the header row), or -1. The framework's own: a scrolled strip is hit-tested against the tabs it
    /// actually drew.
    /// </summary>
    internal static int TabAt(TabControl tabs, int x)
    {
        try
        {
            return TabAtX?.Invoke(tabs, [x]) is int index ? index : -1;
        }
        catch (TargetInvocationException)
        {
            return -1;
        }
    }
}
