using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// The <c>reflow</c> snapshot view: one room as a server that wraps at 78 columns sends it, shown twice in the
/// main window — first as the rows arrived, then through <see cref="ServerWrapJoiner"/> as a world with F5 ▸
/// <c>join wraps</c> on shows it. The demo has no live session, so the joiner is run here over the same rows a
/// session would hand it; <c>WorldSessionReflowTests</c> pins the session doing the same.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The column the demo's server wraps at.</summary>
    internal const int ReflowSceneColumn = 78;

    /// <summary>The demo room, as prose; wrapped at <see cref="ReflowSceneColumn"/> on the way in.</summary>
    internal static readonly string[] ReflowSceneParagraphs =
    [
        "The Grand Bazaar sprawls beneath a canopy of patched sailcloth. Stalls lean against one another in " +
        "crooked rows, their awnings dyed every colour a merchant could afford, and the travelling Merchants " +
        "of the Salt Road call out their prices over the clatter of carts.",
    ];

    /// <summary>
    /// Lays the scene into the main window. Sized to fit an 80x24 pane with both halves showing: one
    /// paragraph and the exits line, which is never joined (it starts with a capital), in each half.
    /// </summary>
    private void LoadReflowScene()
    {
        var rows = new List<StyledLine>();
        foreach (var paragraph in ReflowSceneParagraphs)
        {
            rows.AddRange(GreedyWrap(paragraph, ReflowSceneColumn).Select(r => StyledLine.FromText(r, TextStyle.Default)));
        }

        rows.Add(StyledLine.FromText("Obvious exits: north, south, east and down.", TextStyle.Default));

        AppendWindowLine(MainWindowId, $"[dim]── join wraps off · as sent, wrapped at {ReflowSceneColumn}[/]");
        foreach (var row in rows)
        {
            AppendWindowLine(MainWindowId, _formatter.ToMarkup(row));
        }

        AppendWindowLine(MainWindowId, "[dim]── join wraps on · the same rows, joined[/]");

        // Taught the column the way a session learns it, from lines it has already seen.
        var joiner = new ServerWrapJoiner();
        for (var i = 0; i < ServerWrapJoiner.MinimumSamples; i++)
        {
            foreach (var row in rows)
            {
                joiner.Push(row);
            }
        }

        joiner.Flush();
        foreach (var row in rows)
        {
            foreach (var ready in joiner.Push(row))
            {
                AppendWindowLine(MainWindowId, _formatter.ToMarkup(ready));
            }
        }

        if (joiner.Flush() is { } held)
        {
            AppendWindowLine(MainWindowId, _formatter.ToMarkup(held));
        }
    }

    /// <summary>A greedy word wrap, the way a server does it.</summary>
    internal static List<string> GreedyWrap(string text, int width)
    {
        var rows = new List<string>();
        var row = string.Empty;
        foreach (var word in text.Split(' '))
        {
            if (row.Length == 0)
            {
                row = word;
            }
            else if (row.Length + 1 + word.Length <= width)
            {
                row += " " + word;
            }
            else
            {
                rows.Add(row);
                row = word;
            }
        }

        if (row.Length > 0)
        {
            rows.Add(row);
        }

        return rows;
    }
}
