using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests;

/// <summary>
/// The three things a rule's <c>route</c> can say, and the one it could not say before: <b>nothing</b>.
/// <para>
/// A null <see cref="TriggerActions.SpawnTarget"/> has always meant "this rule adds no destination —
/// the line goes wherever the other matched rules send it". The F2 screen labelled that <c>main</c>,
/// which made it look like a destination, and left the reader with no way to express either half of
/// what they wanted: "highlight it and leave it where it was" looked impossible, and a gagging rule
/// aimed at <c>main</c> deleted the line rather than keeping it there.
/// </para>
/// </summary>
public class TriggerRouteMainTests
{
    private static TriggerEngine EngineWith(params Trigger[] triggers)
    {
        var engine = new TriggerEngine();
        engine.ReplaceConfigured(triggers);
        return engine;
    }

    private static Trigger Rule(string pattern, Action<TriggerActions> configure)
    {
        var trigger = new Trigger { Name = pattern, Pattern = pattern };
        configure(trigger.Actions);
        return trigger;
    }

    private static StyledLine Line(string text) => StyledLine.FromText(text, TextStyle.Default);

    [Test]
    public async Task ARuleWithNoRouteAddsNoDestination()
    {
        var engine = EngineWith(Rule("Ann", a => a.HighlightForeground = TerminalColor.FromRgb(255, 215, 0)));

        var result = engine.Process(Line("Ann waves at you"));

        await Assert.That(result.SpawnTargets).IsEmpty();
        await Assert.That(result.RouteMain).IsFalse();
        await Assert.That(result.Suppress).IsFalse();
    }

    [Test]
    public async Task AHighlightRuleDoesNotMoveALineAnotherRuleRouted()
    {
        // The shape the report was about: a capture rule owns where the line goes, and a highlight rule
        // added afterwards recolours it without changing that. There is one line and one set of
        // destinations, and every matched rule's highlight is on it.
        var engine = EngineWith(
            Rule("^<Chat>", a => { a.SpawnTarget = "Chat"; a.Gag = true; }),
            Rule("Ann", a => a.HighlightForeground = TerminalColor.FromRgb(255, 215, 0)));

        var result = engine.Process(Line("<Chat> Ann says hi"));

        await Assert.That(result.SpawnTargets).IsEquivalentTo(new[] { "Chat" });
        await Assert.That(result.Suppress).IsTrue();
        await Assert.That(result.Line.Spans.Any(s => s.Style.Foreground.Kind == TerminalColorKind.Rgb)).IsTrue();
    }

    [Test]
    public async Task RoutingToMainIsADestinationAndNotTheAbsenceOfOne()
    {
        var engine = EngineWith(Rule("Ann", a => a.SpawnTarget = TriggerActions.MainWindow));

        var result = engine.Process(Line("Ann waves at you"));

        await Assert.That(result.RouteMain).IsTrue();

        // It is *not* a spawn: nothing may go looking for a capture pane called "main", which is what
        // this target used to conjure.
        await Assert.That(result.SpawnTargets).IsEmpty();
    }

    [Test]
    public async Task AGaggingRuleRoutedToMainKeepsTheLineThere()
    {
        // The defect: gag suppresses the *default* delivery, and `main` was not a route, so gag + main
        // deleted the line outright. Explicit destinations survive a gag — which has always been true of
        // a spawn pane, and is the whole meaning of "only where I routed it".
        var engine = EngineWith(Rule("Ann", a =>
        {
            a.SpawnTarget = TriggerActions.MainWindow;
            a.Gag = true;
        }));

        var result = engine.Process(Line("Ann waves at you"));

        await Assert.That(result.Suppress).IsTrue();
        await Assert.That(result.RouteMain).IsTrue();
    }

    [Test]
    public async Task TheMainTargetIsRecognisedWhateverItsCasing()
    {
        // It is a reserved word the user types into a field, not an identifier.
        var engine = EngineWith(Rule("Ann", a => a.SpawnTarget = "Main"));

        await Assert.That(engine.Process(Line("Ann waves")).RouteMain).IsTrue();
    }

    [Test]
    public async Task AWindowGenuinelyCalledMainIsStillUnreachableAsASpawn()
    {
        // Stated so the reservation is a decision rather than an accident: `main` names the session's own
        // window and can name nothing else. No window is titled `main` today — a character's session
        // window is titled after the character, and `main` is only the rail's label for it — so nothing
        // is lost, and a config that already said `main` now reaches what whoever wrote it meant.
        var engine = EngineWith(Rule("Ann", a => a.SpawnTarget = "main"));

        var result = engine.Process(Line("Ann waves"));

        await Assert.That(result.SpawnTargets).IsEmpty();
    }

    [Test]
    public async Task TwoRulesNamingOnePaneDeliverOneLine()
    {
        // They delivered two: the engine appended to a bare list and the session raised one event per
        // entry, so a highlight rule pointed at the same pane as its capture rule doubled every line.
        var engine = EngineWith(
            Rule("^<Chat>", a => a.SpawnTarget = "Chat"),
            Rule("Ann", a => a.SpawnTarget = "Chat"));

        var result = engine.Process(Line("<Chat> Ann says hi"));

        await Assert.That(result.SpawnTargets).IsEquivalentTo(new[] { "Chat" });
    }

    [Test]
    public async Task TwoRulesNamingTwoPanesStillDeliverToBoth()
    {
        // The dedup is by destination and not "one route per line": a line genuinely captured by two
        // channels belongs in both panes.
        var engine = EngineWith(
            Rule("^<Chat>", a => a.SpawnTarget = "Chat"),
            Rule("Ann", a => a.SpawnTarget = "Mentions"));

        var result = engine.Process(Line("<Chat> Ann says hi"));

        await Assert.That(result.SpawnTargets).IsEquivalentTo(new[] { "Chat", "Mentions" });
    }

    [Test]
    public async Task MainAndASpawnAreNotEachOthersDuplicates()
    {
        var engine = EngineWith(
            Rule("^<Chat>", a => a.SpawnTarget = "Chat"),
            Rule("Ann", a => a.SpawnTarget = TriggerActions.MainWindow));

        var result = engine.Process(Line("<Chat> Ann says hi"));

        await Assert.That(result.SpawnTargets).IsEquivalentTo(new[] { "Chat" });
        await Assert.That(result.RouteMain).IsTrue();
    }

    [Test]
    public async Task ARouteTemplateResolvingToMainRoutesToTheMainWindow()
    {
        // Routes expand capture groups, so `main` can arrive from the server's own text. That is fine
        // *because* the main window is the session's own — the destination a line was going to anyway —
        // and it is the one target a resolved name may reach without a user having opened it.
        var engine = EngineWith(Rule(@"^\[(\w+)\]", a => a.SpawnTarget = "$1"));

        await Assert.That(engine.Process(Line("[main] hello")).RouteMain).IsTrue();
    }
}
