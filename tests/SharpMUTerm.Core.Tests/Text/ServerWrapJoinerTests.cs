using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Text;

public class ServerWrapJoinerTests
{
    private const string Prose =
        "The market square is crowded with stalls selling everything from bolts of dyed cloth to " +
        "barrels of salted fish, and the noise of haggling rises above the clatter of carts as " +
        "travelling merchants call out their wares to anyone who will listen, while a pair of " +
        "bored guards lean on their spears by the fountain and watch the crowd for cutpurses and " +
        "trouble of every other kind that a busy morning tends to bring with it.";

    /// <summary>A greedy word wrap, the way a server does it.</summary>
    private static List<string> Wrap(string text, int width)
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

    private static StyledLine Line(string text) => StyledLine.FromText(text, TextStyle.Default);

    /// <summary>Teaches a joiner the wrap column with lines the server filled, and throws away what they produce.</summary>
    private static ServerWrapJoiner Warmed(int width = 78)
    {
        var joiner = new ServerWrapJoiner();
        for (var i = 0; i < ServerWrapJoiner.MinimumSamples; i++)
        {
            joiner.Push(Line(new string('x', width - 1) + "."));
            joiner.Push(Line(new string('y', width - 2) + "."));
        }

        joiner.Flush();
        return joiner;
    }

    private static List<string> Run(ServerWrapJoiner joiner, IEnumerable<string> rows)
    {
        var shown = new List<string>();
        foreach (var row in rows)
        {
            shown.AddRange(joiner.Push(Line(row)).Select(l => l.Text));
        }

        if (joiner.Flush() is { } held)
        {
            shown.Add(held.Text);
        }

        return shown;
    }

    [Test]
    public async Task AParagraphTheServerWrappedComesBackAsOneLine()
    {
        var rows = Wrap(Prose, 78);
        await Assert.That(rows.Count).IsGreaterThan(3);

        var shown = Run(Warmed(), rows);

        await Assert.That(shown).Count().IsEqualTo(1);
        await Assert.That(shown[0]).IsEqualTo(Prose);
    }

    [Test]
    public async Task NothingIsJoinedBeforeTheWrapColumnIsKnown()
    {
        var rows = Wrap(Prose, 78);
        var shown = Run(new ServerWrapJoiner(), rows.Take(3));

        await Assert.That(shown).IsEquivalentTo(rows.Take(3));
    }

    [Test]
    public async Task TheWrapColumnIsTheThirdLongestRecentLine()
    {
        var joiner = new ServerWrapJoiner();
        foreach (var length in new[] { 50, 60, 70, 75, 78, 120, 200, 40 })
        {
            joiner.Push(Line(new string('a', length) + "."));
        }

        // 201 and 121 are outliers; the third longest (79, with its full stop) is the column.
        await Assert.That(joiner.WrapColumn).IsEqualTo(79);
    }

    [Test]
    public async Task AShortStreamHasNoWrapColumn()
    {
        var joiner = new ServerWrapJoiner();
        for (var i = 0; i < 20; i++)
        {
            joiner.Push(Line("You hit the rat."));
        }

        await Assert.That(joiner.WrapColumn).IsNull();
    }

    [Test]
    public async Task ARowEndingInAFullStopIsNotContinuedEvenByALowerCaseLine()
    {
        var first = "The merchants have packed away their stalls for the evening and gone home.";
        var shown = Run(Warmed(), new[] { first, "then the rain begins to fall." });

        await Assert.That(shown).Count().IsEqualTo(2);
    }

    [Test]
    public async Task ALineStartingWithACapitalIsNotAContinuation()
    {
        var rows = new[]
        {
            "The merchants have packed away their stalls for the evening and gone home to",
            "Merchants Row, where the lamps are already lit and the shutters are closed.",
        };

        var shown = Run(Warmed(), rows);

        await Assert.That(shown).IsEquivalentTo(rows);
    }

    [Test]
    public async Task AShortRowIsNeverHeldOrJoined()
    {
        var rows = new[] { "You are hungry", "and thirsty." };
        var joiner = Warmed();

        var first = joiner.Push(Line(rows[0]));

        await Assert.That(joiner.Holding).IsFalse();
        await Assert.That(first.Select(l => l.Text)).IsEquivalentTo(new[] { rows[0] });
    }

    [Test]
    public async Task ARowTheNextWordWouldHaveFittedOnIsNotJoined()
    {
        // 66 characters: "and" would have fitted on it at 78, so the server did not have to break here.
        var first = "The merchants have packed away their stalls for the evening, alone";
        await Assert.That(first.Length).IsLessThan(78 - 4);
        var shown = Run(Warmed(), new[] { first, "and the square is empty." });

        await Assert.That(shown).Count().IsEqualTo(2);
    }

    [Test]
    public async Task AnIndentedNextLineStartsAParagraph()
    {
        var rows = new[]
        {
            "The merchants have packed away their stalls for the evening and gone home to",
            "  supper while the square empties under the first drops of a cold rain.",
        };

        var shown = Run(Warmed(), rows);

        await Assert.That(shown).IsEquivalentTo(rows);
    }

    [Test]
    public async Task AColumnLayoutIsNotProse()
    {
        var rows = new[]
        {
            "Ann        level 12    warrior     online for three hours and some minutes ago",
            "bob        level 3     thief       idle",
        };

        var shown = Run(Warmed(), rows);

        await Assert.That(shown).IsEquivalentTo(rows);
    }

    [Test]
    public async Task AHyphenAtTheBreakIsLeftAlone()
    {
        var rows = new[]
        {
            "The merchants have packed away their stalls for the evening and the well-",
            "known tavern on the corner has opened its doors to the soaking crowd outside.",
        };

        var shown = Run(Warmed(), rows);

        await Assert.That(shown).IsEquivalentTo(rows);
    }

    [Test]
    public async Task AChangeOfStyleAtTheBreakIsNotJoined()
    {
        var joiner = Warmed();
        var first = Line("The merchants have packed away their stalls for the evening and gone home to");
        var red = new TextStyle(TerminalColor.FromIndex(1), TerminalColor.Default, TextAttributes.None);
        var second = StyledLine.FromText("supper while the square empties under the first drops of rain.", red);

        var shown = joiner.Push(first).Concat(joiner.Push(second)).ToList();

        await Assert.That(shown).Count().IsEqualTo(2);
    }

    [Test]
    public async Task AnInteractiveSpanKeepsALineToItself()
    {
        var joiner = Warmed();
        var link = new StyledLine(new[]
        {
            new StyledSpan("The merchants have packed away their stalls for the evening and gone ", TextStyle.Default),
            new StyledSpan("home", TextStyle.Default, new SpanInteraction(InteractionKind.SendCommand, "home")),
        });

        joiner.Push(link);

        await Assert.That(joiner.Holding).IsFalse();
    }

    [Test]
    public async Task APromptIsNeverJoined()
    {
        var prompt = StyledLine.FromText(
            "The merchants have packed away their stalls for the evening and gone home to",
            TextStyle.Default).WithPrompt(true);

        await Assert.That(ServerWrapJoiner.IsFull(prompt, 78)).IsFalse();
    }

    [Test]
    public async Task AJoinKeepsEachSidesStylesAndPutsOneSpaceAtTheBreak()
    {
        var green = new TextStyle(TerminalColor.FromIndex(2), TerminalColor.Default, TextAttributes.None);
        var first = new StyledLine(new[]
        {
            new StyledSpan("A ", TextStyle.Default),
            new StyledSpan("green", green),
            new StyledSpan(" word   ", TextStyle.Default),
        });
        var joined = ServerWrapJoiner.Join(first, Line("continues"));

        await Assert.That(joined.Text).IsEqualTo("A green word continues");
        await Assert.That(joined.Spans[1].Style).IsEqualTo(green);
    }

    [Test]
    public async Task AFullRowFollowedByABlankLineIsShownAsItWas()
    {
        var full = "The merchants have packed away their stalls for the evening and gone home to";
        var shown = Run(Warmed(), new[] { full, string.Empty, "supper." });

        await Assert.That(shown).IsEquivalentTo(new[] { full, string.Empty, "supper." });
    }

    [Test]
    public async Task ANarrowerServerReflowsToo()
    {
        var rows = Wrap(Prose, 60);
        var shown = Run(Warmed(60), rows);

        await Assert.That(shown).IsEquivalentTo(new[] { Prose });
    }

    [Test]
    public async Task TwoParagraphsStayTwo()
    {
        var second = "Somewhere beyond the walls a bell is ringing for the evening meal, and the gulls wheel " +
                     "over the harbour crying for scraps from the boats that came in with the tide.";
        var rows = Wrap(Prose, 78).Concat(Wrap(second, 78)).ToList();

        var shown = Run(Warmed(), rows);

        await Assert.That(shown).IsEquivalentTo(new[] { Prose, second });
    }
}
