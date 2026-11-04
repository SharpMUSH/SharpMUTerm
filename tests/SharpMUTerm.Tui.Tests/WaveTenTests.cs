using System.Text.RegularExpressions;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The pins for the ux-wave9 audit's findings this wave closed: boundary bars as wide as their pane (#3), a pane
/// line's plain text read from its spans (#9), F4's keys in glyphs (#13), the numpad in the window (#19), the
/// collapsed rail's initials and world mark (#17) and the ⌃F and F4 footers' drop-order ladders (#23).
/// </summary>
[NotInParallel]
public class WaveTenTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp Demo(int width, int height, string? view = null)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(width, height));
        app.RenderSnapshot(view);
        return app;
    }

    // ── #3: the bars fill their pane ───────────────────────────────────────────────────────────────

    [Test]
    [Arguments(30)]
    [Arguments(72)]
    [Arguments(140)]
    public async Task EveryBoundaryBarIsExactlyAsWideAsItsPane(int width)
    {
        string[] bars =
        [
            AwayBarRenderer.Bar(37, TimeSpan.FromMinutes(12), "#c678dd", width),
            AwayBarRenderer.Missed(3, "#c678dd", width),
            SearchBarRenderer.Bar("goblin", 2, 3, "#c678dd", width),
            RestoreBarRenderer.Bar(500, DateTimeOffset.Now, "#c678dd", width),
        ];

        foreach (var bar in bars)
        {
            await Assert.That(MarkupText.VisibleLength(bar)).IsEqualTo(width).Because(bar);
        }
    }

    [Test]
    public async Task ANarrowPaneElidesTheWordsBeforeTheBarCouldWrap()
    {
        var bar = AwayBarRenderer.Bar(37, TimeSpan.FromMinutes(12), "#c678dd", 24);
        await Assert.That(MarkupText.VisibleLength(bar)).IsEqualTo(24);
        await Assert.That(bar).Contains("…");
        await Assert.That(bar).Contains(AwayBarRenderer.Label);
    }

    [Test]
    public async Task AnUnarrangedPaneKeepsTheOldRule()
    {
        var bar = AwayBarRenderer.Missed(3, "#c678dd");
        await Assert.That(bar).Contains(new string('─', BarRule.Fallback));
    }

    /// <summary>The live bar is drawn at the pane's viewport width, and redrawn when that width changes.</summary>
    [Test]
    public async Task TheActivityBarSpansThePaneAt80Columns()
    {
        await using var app = Demo(80, 24, "activity-bar");
        var row = app.PaneLines("main").Single(l => l.Contains(AwayBarRenderer.MissedLabel, StringComparison.Ordinal));
        var width = app.BarWidth("main");
        await Assert.That(width).IsGreaterThan(0);
        await Assert.That(MarkupText.VisibleLength(row)).IsEqualTo(width);
    }

    // ── #9: plain text is the line's own ───────────────────────────────────────────────────────────

    [Test]
    public async Task AHighlightedLineStillMatchesItsOwnAnchoredPattern()
    {
        await using var app = Demo(120, 32);
        var lines = app.TriggerTestLines();
        await Assert.That(lines.Any(l => Regex.IsMatch(l, @"^\[public\] (.+)$"))).IsTrue();
        await Assert.That(lines.Any(l => l.Contains('▌', StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task SearchResultsShowNoRuleGlyph()
    {
        await using var app = Demo(120, 32);
        var model = app.SearchModelFor("Rivane: to the crypt", regex: false, all: true);
        await Assert.That(model.Rows.Count).IsGreaterThan(0);
        foreach (var row in model.Rows)
        {
            await Assert.That(row.Text).DoesNotContain("▌");
        }
    }

    // ── #13 / #19: F4's keys ────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task F4SpellsABindingsKeyInGlyphs()
    {
        var macros = new List<Macro> { new() { Name = "score", Key = "Ctrl+F1", Command = "score", Enabled = true } };
        var lines = KeypadScreenRenderer.HotkeysColumn(HostKeys.Terminal, macros);
        await Assert.That(lines.Any(l => l.Contains("⌃F1", StringComparison.Ordinal))).IsTrue();
        await Assert.That(lines.Any(l => l.Contains("Ctrl+F1", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task ANumpadKeyFiresOnlyOnAHostThatDeliversIt()
    {
        var window = HostKeys.Terminal with { ReportsChordsWhole = true, DeliversNumpad = true };
        await Assert.That(MacroKeys.Verdict(HostKeys.Terminal, "Num7").Fires).IsFalse();
        await Assert.That(MacroKeys.Verdict(window, "Num7").Fires).IsTrue();
    }

    // ── #17: the collapsed rail ─────────────────────────────────────────────────────────────────────

    [Test]
    public async Task TheCollapsedRailGivesTwoLettersOnlyWhenTwoNamesShareOne()
    {
        await Assert.That(RailRenderer.InitialLength(["Corvid", "Rivane", "Thorn"])).IsEqualTo(1);
        await Assert.That(RailRenderer.InitialLength(["Corvid", "Rivane", "Riko"])).IsEqualTo(2);
        await Assert.That(RailRenderer.Initials("Riko", 2)).IsEqualTo("Ri");
        await Assert.That(RailRenderer.Initials("R", 2)).IsEqualTo("R ");
    }

    [Test]
    public async Task TwoSameInitialCharactersCanBeToldApartOnTheCollapsedRail()
    {
        var rows = new List<RailRow>
        {
            new(RailRowKind.World, 0, "Aetherfall", Target: "rail:w"),
            new(RailRowKind.Character, 1, "Rivane", Target: "rail:a"),
            new(RailRowKind.Character, 1, "Riko", Target: "rail:b"),
        };
        var lines = RailRenderer.RenderCollapsed(rows);
        var text = lines.Select(MarkupText.Plain).ToList();
        await Assert.That(text.Any(l => l.Contains("Ri", StringComparison.Ordinal) && l.Contains("○", StringComparison.Ordinal))).IsTrue();
        await Assert.That(text[0]).Contains(RailRenderer.CollapsedWorldMark);
        await Assert.That(text[1].Length).IsEqualTo(text[2].Length);
        await Assert.That(string.Join("\n", text)).DoesNotContain("▚");
    }

    // ── #23: footers do not clip at 80 ──────────────────────────────────────────────────────────────

    [Test]
    public async Task TheSearchFooterFitsAnEightyColumnSurfaceWholeKeysAndAll()
    {
        var footer = SearchPrompt.HintsFor(74);
        await Assert.That(footer.Length).IsLessThanOrEqualTo(74);
        await Assert.That(footer).EndsWith("⌃F closes");
        foreach (var key in new[] { "↑↓", "⏎", "⌥E", "⌥A", "Esc", "⌃F" })
        {
            await Assert.That(SearchPrompt.HintsFor(SearchSurface.MinimumWidth)).Contains(key);
        }
    }
}
