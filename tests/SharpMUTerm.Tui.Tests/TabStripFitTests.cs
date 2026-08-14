using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// How much of each tab's name a strip can afford. The rule only — <see cref="TabStripElisionTests"/>
/// drives it through a real strip in a real pane, which is where the cell counting is checked against a
/// frame rather than against this file's arithmetic.
/// </summary>
public class TabStripFitTests
{
    /// <summary>
    /// One tab: <paramref name="name"/> is what it would draw whole, and the part after the last
    /// <c>" - "</c> is its title — the same split <c>TabTitles.Name</c> makes. <paramref name="extra"/>
    /// is what the tab spends besides its name; three is the ordinary case (a space either side, and the
    /// separator that follows it).
    /// </summary>
    private static TabCost Tab(string name, bool selected = false, int extra = 3)
    {
        var at = name.LastIndexOf(" - ", StringComparison.Ordinal);
        return new TabCost(name.Length, at >= 0 ? name.Length - at - 3 : name.Length, extra, selected);
    }

    private static IReadOnlyList<int> Budgets(int width, params TabCost[] tabs) =>
        TabStripFit.Budgets(tabs, width);

    /// <summary>A strip with room is left exactly alone — every budget is the whole name.</summary>
    [Test]
    public async Task AStripThatFitsIsNotElidedAtAll()
    {
        var tabs = new[] { Tab("Corvid - Chat"), Tab("Corvid - Public", selected: true) };

        await Assert.That(Budgets(100, tabs)).IsEquivalentTo(new[] { 13, 15 });
    }

    /// <summary>
    /// A strip that has not been arranged yet reports no width, and that is not the same as having no
    /// room: eliding against it would cut every title on the first frame and undo it on the second.
    /// </summary>
    [Test]
    public async Task AnUnarrangedStripElidesNothing()
    {
        var tabs = new[] { Tab("Corvid - Chat"), Tab("Corvid - O-Gatecrashers", selected: true) };

        await Assert.That(Budgets(0, tabs)).IsEquivalentTo(new[] { 13, 23 });
    }

    /// <summary>
    /// The first thing a crowded strip drops, and it drops it for everybody: a pane full of one
    /// character's captures repeats the same prefix on every tab, which is the cheapest thing there is to
    /// lose and the most there is of it. Nobody loses a letter of their own name to it.
    /// </summary>
    [Test]
    public async Task TheOwnerPrefixGoesBeforeAnyNameIsCut()
    {
        var tabs = new[]
        {
            Tab("Corvid - Chat", selected: true),
            Tab("Corvid - Public"),
            Tab("Corvid - Tells"),
        };

        // 4 + 6 + 5 names, 9 of frame: the titles fit exactly where the prefixed names could not.
        await Assert.That(Budgets(24, tabs)).IsEquivalentTo(new[] { 4, 6, 5 });
    }

    /// <summary>
    /// Cells come off the long name and leave the short one whole. Shrinking in proportion would take a
    /// slice off <c>Chat</c> as well, which buys the strip nothing and costs it a word.
    /// </summary>
    [Test]
    public async Task TheLongNameGivesUpCellsAndTheShortOneKeepsThemAll()
    {
        var tabs = new[] { Tab("Chat"), Tab("O-Gatecrashers"), Tab("Announcements") };

        var budgets = Budgets(30, tabs);

        await Assert.That(budgets[0]).IsEqualTo(4);
        await Assert.That(budgets[1]).IsEqualTo(budgets[2]);
        await Assert.That(budgets[1]).IsLessThan(14);
        await Assert.That(budgets.Sum() + 9).IsLessThanOrEqualTo(30);
    }

    /// <summary>
    /// The tab the pane is showing keeps its whole title while any other tab still has cells to give. It
    /// is the one the reader is looking at and the one the strip exists to name.
    /// </summary>
    [Test]
    public async Task TheSelectedTabIsSparedWhileTheOthersCanStillPay()
    {
        var tabs = new[]
        {
            Tab("Announcements"),
            Tab("O-Gatecrashers", selected: true),
            Tab("Radio Umi"),
        };

        var budgets = Budgets(34, tabs);

        await Assert.That(budgets[1]).IsEqualTo(14);
        await Assert.That(budgets[0]).IsLessThan(13);
    }

    /// <summary>
    /// And when it cannot be spared it still keeps enough to be read. A strip where every label including
    /// the selected one is three letters and an ellipsis answers none of the questions a strip is for.
    /// <para>
    /// <b>The floors are where this stops, and past them the tabs really do overflow.</b> Eight tabs
    /// cannot be drawn in forty cells however they are labelled, so the answer is the floors and the
    /// framework clips whatever is left over — which is the state that existed before any of this and is
    /// simply reached later now. What is bought is the tabs that do fit: at the floors, six of these
    /// eight are drawn where two of them were before.
    /// </para>
    /// </summary>
    [Test]
    public async Task EvenAStripThatCannotFitKeepsTheSelectedTabReadable()
    {
        var tabs = Enumerable.Range(0, 8)
            .Select(i => Tab($"Channel number {i}", selected: i == 3))
            .ToArray();

        var budgets = Budgets(40, tabs);

        await Assert.That(budgets[3]).IsEqualTo(TabStripFit.MinimumSelectedName);
        await Assert.That(budgets.Where((_, i) => i != 3).Max()).IsEqualTo(TabStripFit.MinimumName);
    }

    /// <summary>
    /// No budget is ever negative or past the name it is for — the two ways an elision this feeds could
    /// throw rather than merely look wrong.
    /// </summary>
    [Test]
    public async Task NoBudgetIsNegativeOrLongerThanTheNameItIsFor()
    {
        var tabs = new[] { Tab("A"), Tab("Corvid - B", selected: true), Tab("Corvid - Announcements") };

        foreach (var width in new[] { 0, 1, 5, 12, 30, 400 })
        {
            var budgets = Budgets(width, tabs);
            for (var i = 0; i < tabs.Length; i++)
            {
                await Assert.That(budgets[i]).IsGreaterThanOrEqualTo(1);
                await Assert.That(budgets[i]).IsLessThanOrEqualTo(tabs[i].NameLength);
            }
        }
    }
}
