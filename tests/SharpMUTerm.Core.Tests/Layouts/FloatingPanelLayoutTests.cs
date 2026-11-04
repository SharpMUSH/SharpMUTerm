using System.Text.Json;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Tests.Layouts;

/// <summary>
/// The host-neutral half of a floating panel: <see cref="FloatingBounds"/> (and its clamp to the screens there are),
/// <see cref="WorkspaceWindow.Floating"/> carried through a saved workspace, <see cref="PanelPlacement.Floating"/>
/// captured into a layout, and a fold that does not read it, so a host that cannot float keeps the panel in its region.
/// </summary>
public class FloatingPanelLayoutTests
{
    private static readonly FloatingBounds Left = new(0, 0, 1920, 1080);
    private static readonly FloatingBounds Right = new(1920, 0, 2560, 1440);

    // ── Clamping ─────────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task BoundsWhollyOnAScreenAreLeftAlone()
    {
        var onSecond = new FloatingBounds(2400, 200, 500, 400);
        await Assert.That(onSecond.ClampTo([Left, Right])).IsEqualTo(onSecond);
    }

    [Test]
    public async Task BoundsOnAScreenThatIsGoneComeBackOnTheNearestOne()
    {
        var onSecond = new FloatingBounds(2400, 200, 500, 400);
        await Assert.That(onSecond.ClampTo([Left])).IsEqualTo(new FloatingBounds(1420, 200, 500, 400));

        var aboveEverything = new FloatingBounds(-900, -700, 300, 200);
        await Assert.That(aboveEverything.ClampTo([Left])).IsEqualTo(new FloatingBounds(0, 0, 300, 200));
    }

    [Test]
    public async Task BoundsAcrossTwoScreensGoToTheOneHoldingMostOfThem()
    {
        var straddling = new FloatingBounds(1800, 100, 400, 300);
        await Assert.That(straddling.ClampTo([Left, Right])).IsEqualTo(new FloatingBounds(1920, 100, 400, 300));
    }

    [Test]
    public async Task BoundsLargerThanTheScreenAreShrunkToIt()
    {
        var huge = new FloatingBounds(100, 100, 4000, 3000);
        await Assert.That(huge.ClampTo([Left])).IsEqualTo(Left);
    }

    [Test]
    public async Task UnplacedBoundsAndNoScreensChangeNothing()
    {
        await Assert.That(FloatingBounds.Unplaced.ClampTo([Left])).IsEqualTo(FloatingBounds.Unplaced);
        var somewhere = new FloatingBounds(5000, 5000, 300, 200);
        await Assert.That(somewhere.ClampTo([])).IsEqualTo(somewhere);
    }

    // ── The workspace and the layout carry it ───────────────────────────────────────────────────

    [Test]
    public async Task AFloatingWindowSurvivesASavedWorkspace()
    {
        var workspace = new Workspace("main", "Main");
        workspace.OpenWindow("map:W.C", "Map", WindowKind.Auxiliary, "W.C");
        workspace.FindWindow("map:W.C")!.Floating = new FloatingBounds(1200, 80, 460, 460);

        var json = JsonSerializer.Serialize(WorkspaceState.Capture(workspace));
        var restored = JsonSerializer.Deserialize<WorkspaceState>(json)!.Restore();

        await Assert.That(restored.FindWindow("map:W.C")!.Floating).IsEqualTo(new FloatingBounds(1200, 80, 460, 460));
        await Assert.That(restored.FindWindow("main")!.Floating).IsNull();
        await Assert.That(json).DoesNotContain("IsPlaced");
    }

    [Test]
    public async Task CapturingAFloatingPanelKeepsItsRegionAndSaysWhereItFloats()
    {
        var fold = LayoutFold.Fold(BuiltInLayouts.Combat, new PanelAvailability(RoomsRecorded: 3, MeterValueArrived: true, SpawnWindows: 0));
        var map = new FloatingBounds(1200, 80, 460, 460);

        // The map floats, so the realiser was handed no window for it and its pane is gone.
        var realised = LayoutRealiser.Realise(
            BuiltInLayouts.Combat,
            fold,
            panel => panel switch { PanelIds.Output => ["main"], PanelIds.Vitals => ["panel:vitals:W.C"], _ => [] },
            "main")!;
        string? PanelOf(string window) => window switch { "main" => PanelIds.Output, "panel:vitals:W.C" => PanelIds.Vitals, _ => null };

        var captured = LayoutCapture.Capture(
            realised.Root, PanelOf, BuiltInLayouts.Combat, new Dictionary<string, FloatingBounds> { [PanelIds.Map] = map })!;
        var placement = captured.Placements.Single(p => p.PanelId == PanelIds.Map);
        await Assert.That(placement.Floating).IsEqualTo(map);
        await Assert.That(placement.Region).IsEqualTo(BuiltInLayouts.Combat.PlacementOf(PanelIds.Map)!.Region)
            .Because("docking back returns it to the region it was taken from");
        await Assert.That(captured.Placements.Single(p => p.PanelId == PanelIds.Vitals).Floating).IsNull();

        // A host that cannot float captures with no floating list, and keeps what the layout already said.
        var floats = BuiltInLayouts.Combat with
        {
            Placements = BuiltInLayouts.Combat.Placements.Select(p => p.PanelId == PanelIds.Map ? p with { Floating = map } : p).ToList(),
        };
        var terminal = LayoutCapture.Capture(realised.Root, PanelOf, floats)!;
        await Assert.That(terminal.Placements.Single(p => p.PanelId == PanelIds.Map).Floating).IsEqualTo(map);
    }

    [Test]
    public async Task TheFoldDoesNotReadFloating()
    {
        var availability = new PanelAvailability(RoomsRecorded: 3, MeterValueArrived: true, SpawnWindows: 2);
        var floats = BuiltInLayouts.Combat with
        {
            Placements = BuiltInLayouts.Combat.Placements
                .Select(p => p with { Floating = new FloatingBounds(10, 10, 300, 200) }).ToList(),
        };

        var plain = LayoutFold.Fold(BuiltInLayouts.Combat, availability);
        var floating = LayoutFold.Fold(floats, availability);
        await Assert.That(LayoutRealiser.FoldShape(floating)).IsEqualTo(LayoutRealiser.FoldShape(plain));
    }

    [Test]
    public async Task AFloatingPlacementRoundTripsThroughJson()
    {
        var placement = new PanelPlacement { PanelId = PanelIds.Vitals, Region = LayoutRegion.TopStrip, Floating = new FloatingBounds(5, 6, 300, 120) };
        var json = JsonSerializer.Serialize(placement);
        await Assert.That(JsonSerializer.Deserialize<PanelPlacement>(json)).IsEqualTo(placement);

        var docked = new PanelPlacement { PanelId = PanelIds.Vitals };
        await Assert.That(JsonSerializer.Deserialize<PanelPlacement>(JsonSerializer.Serialize(docked))!.Floating).IsNull();
    }
}
