using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Tests.Layouts;

/// <summary>
/// <see cref="LayoutRealiser"/> turns a fold into a workspace tree of windows, <see cref="LayoutCapture"/> reads an
/// arrangement back into placements, and <see cref="WorkspaceLayout.ReplaceTree"/> swaps the result in.
/// </summary>
public class LayoutRealiserTests
{
    private static readonly IReadOnlyDictionary<string, string[]> Windows = new Dictionary<string, string[]>
    {
        [PanelIds.Output] = ["main"],
        [PanelIds.Map] = ["map:W.C"],
        [PanelIds.Chat] = ["spawn:chat", "spawn:ooc"],
        [PanelIds.Vitals] = ["panel:vitals:W.C"],
    };

    private static IReadOnlyList<string> WindowsOf(string panel) => Windows.GetValueOrDefault(panel) ?? [];

    [Test]
    public async Task CombatPutsTheMapBesideTheOutputAndTheCollapsedChatUnderTheMapAsTabs()
    {
        var fold = LayoutFold.Fold(BuiltInLayouts.Combat, new PanelAvailability(RoomsRecorded: 3, MeterValueArrived: true, SpawnWindows: 2));
        var realised = LayoutRealiser.Realise(BuiltInLayouts.Combat, fold, WindowsOf, "main")!;

        var panes = realised.Root.Panes().ToDictionary(p => p.Id);
        await Assert.That(panes["panel:map"].Tabs).IsEquivalentTo(new[] { "map:W.C", "spawn:chat", "spawn:ooc" });
        await Assert.That(panes["panel:map"].ActiveTab).IsEqualTo("map:W.C");
        await Assert.That(panes["panel:vitals"].Tabs).IsEquivalentTo(new[] { "panel:vitals:W.C" });
        // The quick bar has no window, so its pane is dropped rather than drawn empty.
        await Assert.That(panes.ContainsKey("panel:quick-bar")).IsFalse();
        await Assert.That(realised.FocusedPaneId).IsEqualTo("panel:output");
        await Assert.That(realised.PanelPanes[PanelIds.Chat]).IsEqualTo("panel:map");
    }

    [Test]
    public async Task WindowsNoPanelPlacesAreKeptInThePaneTheyAskForOrTheOutputPane()
    {
        var fold = LayoutFold.Fold(BuiltInLayouts.Combat, new PanelAvailability(RoomsRecorded: 3));
        var realised = LayoutRealiser.Realise(
            BuiltInLayouts.Combat, fold, WindowsOf, "main",
            [("web", "panel:map"), ("char:Other.Ann", "p7"), ("main", null)])!;

        var panes = realised.Root.Panes().ToDictionary(p => p.Id);
        await Assert.That(panes["panel:map"].Tabs).Contains("web");
        await Assert.That(panes["panel:output"].Tabs).IsEquivalentTo(new[] { "main", "char:Other.Ann" });
        await Assert.That(panes["panel:output"].ActiveTab).IsEqualTo("main");
    }

    [Test]
    public async Task TheShapeChangesOnlyWhenWhatIsDrawnChanges()
    {
        var quiet = LayoutFold.Fold(BuiltInLayouts.Combat, new PanelAvailability());
        var stillQuiet = LayoutFold.Fold(BuiltInLayouts.Combat, new PanelAvailability(GameDataEntries: 40));
        var mapped = LayoutFold.Fold(BuiltInLayouts.Combat, new PanelAvailability(RoomsRecorded: 1));

        await Assert.That(LayoutRealiser.FoldShape(stillQuiet)).IsEqualTo(LayoutRealiser.FoldShape(quiet));
        await Assert.That(LayoutRealiser.FoldShape(mapped)).IsNotEqualTo(LayoutRealiser.FoldShape(quiet));
    }

    [Test]
    public async Task ReplacingTheTreeClearsTheZoomAndFocusesTheNamedPane()
    {
        var layout = new WorkspaceLayout(["main", "web"]);
        layout.SplitFocused(SplitDirection.Row);
        layout.ToggleZoom();
        var fold = LayoutFold.Fold(BuiltInLayouts.Combat, new PanelAvailability(RoomsRecorded: 1));
        var realised = LayoutRealiser.Realise(BuiltInLayouts.Combat, fold, WindowsOf, "main", [("web", null)])!;

        layout.ReplaceTree(realised.Root, realised.FocusedPaneId);

        await Assert.That(layout.ZoomedPaneId).IsNull();
        await Assert.That(layout.FocusedPaneId).IsEqualTo("panel:output");
        await Assert.That(layout.FindWindow("map:W.C")!.Id).IsEqualTo("panel:map");
        // A split after the swap gets a fresh pN id, and the panes keep the fold's numbering: main first.
        layout.SplitFocused(SplitDirection.Column);
        await Assert.That(layout.Panes.Select(p => p.Id).Distinct().Count()).IsEqualTo(layout.Panes.Count);
        await Assert.That(layout.Panes[0].Id).IsEqualTo("panel:output");
    }

    [Test]
    public async Task CapturingTheCombatArrangementGivesBackItsRegions()
    {
        var fold = LayoutFold.Fold(BuiltInLayouts.Combat, new PanelAvailability(RoomsRecorded: 3, MeterValueArrived: true, SpawnWindows: 2));
        var realised = LayoutRealiser.Realise(BuiltInLayouts.Combat, fold, WindowsOf, "main")!;
        string? PanelOf(string window) => Windows.FirstOrDefault(kv => kv.Value.Contains(window)).Key;

        var captured = LayoutCapture.Capture(realised.Root, PanelOf, BuiltInLayouts.Combat)!;

        var byId = captured.Placements.ToDictionary(p => p.PanelId);
        await Assert.That(byId[PanelIds.Output].Region).IsEqualTo(LayoutRegion.Main);
        await Assert.That(byId[PanelIds.Map].Region).IsEqualTo(LayoutRegion.Side);
        await Assert.That(byId[PanelIds.Vitals].Region).IsEqualTo(LayoutRegion.Side);
        await Assert.That(byId[PanelIds.Vitals].Order).IsLessThan(byId[PanelIds.Map].Order);
        await Assert.That(byId[PanelIds.Chat].TabOf).IsEqualTo(PanelIds.Map);
        // Not on screen (no window draws it), so it keeps the placement the layout gave it.
        await Assert.That(byId[PanelIds.QuickBar].Region).IsEqualTo(LayoutRegion.Bottom);
        await Assert.That(captured.RegionShares[LayoutRegion.Side]).IsBetween(0.25, 0.35);

        // And folding the capture again draws the same panes.
        var again = LayoutFold.Fold(
            new LayoutDefinition { Id = "x", Name = "x", Placements = captured.Placements, RegionShares = captured.RegionShares },
            new PanelAvailability(RoomsRecorded: 3, MeterValueArrived: true, SpawnWindows: 2));
        var panesAgain = again.Root!.Panes().Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal);
        var panesBefore = realised.Root.Panes().Select(p => p.Id).Append("panel:quick-bar").OrderBy(id => id, StringComparer.Ordinal);
        await Assert.That(panesAgain).IsEquivalentTo(panesBefore);
    }

    [Test]
    public async Task NothingIsCapturedWithoutTheOutput()
    {
        var tree = new PaneNode("p1", ["web"]);
        await Assert.That(LayoutCapture.Capture(tree, _ => null)).IsNull();
    }
}
