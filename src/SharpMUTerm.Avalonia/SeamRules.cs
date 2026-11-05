using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SharpMUTerm.Avalonia;

/// <summary>A horizontal rule a native view draws across its whole width, in its own coordinates, and its colour.</summary>
internal readonly record struct EdgeRule(Rect Rect, Color Colour);

/// <summary>
/// A native view placed over a pane that draws rules from one side edge to the other (a tab strip's foot, a panel's
/// heading, a strip's seams): it says where, so <see cref="SeamRules"/> can carry each one on to a divider's hairline.
/// </summary>
internal interface IEdgeRules
{
    /// <summary>The full-width rules the view draws at <paramref name="size"/>, in its own coordinates; none while it draws none.</summary>
    IEnumerable<EdgeRule> EdgeRules(Size size);
}

/// <summary>
/// The pieces of rule between a native view's edge and the hairline of the pane divider beside it. A native view covers
/// only its pane; the divider's cell is the cells', painted by <see cref="TerminalView"/> as the two planes meeting at a
/// hairline in its middle, so a view's rule stopped half a cell short of it. This draws that half cell of each rule, in
/// the view's own colour, so the rules meet the hairline as the mockups' do. It takes no input
/// (<see cref="InputElement.IsHitTestVisible"/> false): a press there is still on the divider's cells, so its hover,
/// drag and double-click are the client's, unchanged, and no cell, pane or NAWS moves.
/// </summary>
internal sealed class SeamRules : Control
{
    private IReadOnlyList<EdgeRule> _runs = [];

    public SeamRules()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>What it draws, in its own coordinates. Internal for tests.</summary>
    internal IReadOnlyList<EdgeRule> Runs => _runs;

    /// <summary>Sets the runs, repainting only when they changed.</summary>
    internal void Show(IReadOnlyList<EdgeRule> runs)
    {
        if (runs.SequenceEqual(_runs))
        {
            return;
        }

        _runs = runs;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        foreach (var run in _runs)
        {
            context.FillRectangle(new SolidColorBrush(run.Colour), run.Rect);
        }
    }

    /// <summary>
    /// The runs for <paramref name="views"/> (each with its rectangle in the same coordinates as
    /// <paramref name="hairlines"/>): every rule of a view whose side edge is within a cell (<paramref name="cellWidth"/>)
    /// of a vertical hairline it overlaps, from the edge to the hairline. Both sides of a divider meet it when both are native.
    /// </summary>
    internal static List<EdgeRule> Between(IEnumerable<(Rect Area, IEnumerable<EdgeRule> Rules)> views, IReadOnlyList<Rect> hairlines, double cellWidth)
    {
        var runs = new List<EdgeRule>();
        if (hairlines.Count == 0)
        {
            return runs;
        }

        const double slack = 0.5;
        foreach (var (area, rules) in views)
        {
            foreach (var rule in rules)
            {
                var top = area.Y + rule.Rect.Y;
                var bottom = top + rule.Rect.Height;
                foreach (var hair in hairlines)
                {
                    if (top < hair.Top - slack || bottom > hair.Bottom + slack)
                    {
                        continue;
                    }

                    var leftGap = area.X - hair.Right;
                    if (leftGap > 0.01 && leftGap <= cellWidth + slack)
                    {
                        runs.Add(new EdgeRule(new Rect(hair.Right, top, leftGap, rule.Rect.Height), rule.Colour));
                    }

                    var rightGap = hair.Left - area.Right;
                    if (rightGap > 0.01 && rightGap <= cellWidth + slack)
                    {
                        runs.Add(new EdgeRule(new Rect(area.Right, top, rightGap, rule.Rect.Height), rule.Colour));
                    }
                }
            }
        }

        return runs;
    }
}
