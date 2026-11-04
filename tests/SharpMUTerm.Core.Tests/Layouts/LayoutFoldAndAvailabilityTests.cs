using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Tests.Layouts;

/// <summary>
/// <see cref="PanelAvailability"/> decides which placed panels are drawn, and <see cref="LayoutFold"/> folds what is
/// drawn into the workspace's own split tree.
/// </summary>
public class LayoutFoldAndAvailabilityTests
{
    [Test]
    public async Task OnlyTheDatalessPanelsAreAvailableBeforeAnythingArrives()
    {
        var nothing = PanelAvailability.Nothing;

        foreach (var always in new[] { PanelIds.Output, PanelIds.Composer, PanelIds.QuickBar })
        {
            await Assert.That(nothing.HasData(always)).IsTrue();
        }

        foreach (var needsData in new[] { PanelIds.Map, PanelIds.Vitals, PanelIds.WhoHere, PanelIds.Chat, PanelIds.GameData, "hologram" })
        {
            await Assert.That(nothing.HasData(needsData)).IsFalse();
        }
    }

    [Test]
    public async Task EachPanelLightsUpWithItsOwnData()
    {
        await Assert.That(new PanelAvailability(RoomsRecorded: 1).HasData(PanelIds.Map)).IsTrue();
        await Assert.That(new PanelAvailability(MeterValueArrived: true).HasData(PanelIds.Vitals)).IsTrue();
        await Assert.That(new PanelAvailability(WhoHereKnown: true).HasData(PanelIds.WhoHere)).IsTrue();
        await Assert.That(new PanelAvailability(SpawnWindows: 2).HasData(PanelIds.Chat)).IsTrue();
        await Assert.That(new PanelAvailability(GameDataEntries: 3).HasData(PanelIds.GameData)).IsTrue();
        await Assert.That(new PanelAvailability(RoomsRecorded: 1).HasData(PanelIds.Vitals)).IsFalse();
    }

    [Test]
    public async Task AvailabilityIsReadOffTheGameDataStore()
    {
        var data = new GameDataStore();
        await Assert.That(PanelAvailability.From(data, 0, 0)).IsEqualTo(PanelAvailability.Nothing);

        data.SetGmcp("Room.Players", "[]");
        data.SetGmcp("Char.Vitals", """{"hp":"450","maxhp":"500"}""");
        var seen = PanelAvailability.From(data, roomsRecorded: 4, spawnWindows: 1);

        await Assert.That(seen.WhoHereKnown).IsTrue();
        await Assert.That(seen.MeterValueArrived).IsTrue();
        await Assert.That(seen.RoomsRecorded).IsEqualTo(4);
        await Assert.That(seen.SpawnWindows).IsEqualTo(1);
        await Assert.That(seen.GameDataEntries).IsEqualTo(2);
        await Assert.That(PanelAvailability.From(null, 0, 0)).IsEqualTo(PanelAvailability.Nothing);
    }

    [Test]
    public async Task RoleplayWithEverythingFoldsIntoOutputWithChatBehindItAndAColumnOfComposerOverWhoHere()
    {
        var fold = LayoutFold.Fold(BuiltInLayouts.Roleplay);

        // Row[ output+chat, Column[ composer, who-here ] ]
        var root = (SplitNode)fold.Root!;
        await Assert.That(root.Direction).IsEqualTo(SplitDirection.Row);
        await Assert.That(root.Sizes[1]).IsEqualTo(0.35).Within(1e-9);

        var main = (PaneNode)root.Children[0];
        await Assert.That(main.Tabs).IsEquivalentTo(new[] { PanelIds.Output, PanelIds.Chat });

        var side = (SplitNode)root.Children[1];
        await Assert.That(side.Direction).IsEqualTo(SplitDirection.Column);
        await Assert.That(side.Panes().Select(p => p.Id)).IsEquivalentTo(new[] { "panel:composer", "panel:who-here" });
        await Assert.That(side.Sizes[0]).IsGreaterThan(side.Sizes[1]);
        await Assert.That(fold.Collapsed).IsEmpty();
        await Assert.That(fold.Waiting).IsEmpty();
    }

    [Test]
    public async Task ATabWhoseHostHasNoDataTakesAPaneOfItsOwn()
    {
        // A MUSH sends no Room.Players, but trigger rules have opened a channel window.
        var layout = new LayoutDefinition
        {
            Id = "x",
            Name = "X",
            Placements =
            [
                new() { PanelId = PanelIds.Output },
                new() { PanelId = PanelIds.WhoHere, Region = LayoutRegion.Side, Order = 0 },
                new() { PanelId = PanelIds.Chat, Region = LayoutRegion.Side, Order = 1, TabOf = PanelIds.WhoHere },
            ],
        };
        var fold = LayoutFold.Fold(layout, new PanelAvailability(SpawnWindows: 1));

        var panes = fold.Root!.Panes().ToList();
        await Assert.That(panes.Select(p => p.Id)).IsEquivalentTo(new[] { "panel:output", "panel:chat" });
        await Assert.That(panes[1].Tabs).IsEquivalentTo(new[] { PanelIds.Chat });
        await Assert.That(fold.Waiting.Select(p => p.PanelId)).IsEquivalentTo(new[] { PanelIds.WhoHere });
    }

    [Test]
    public async Task CombatWithNoDataYetIsOutputAndTheQuickBar()
    {
        var fold = LayoutFold.Fold(BuiltInLayouts.Combat, PanelAvailability.Nothing);

        await Assert.That(fold.Root!.Panes().Select(p => p.Id)).IsEquivalentTo(new[] { "panel:output", "panel:quick-bar" });
        await Assert.That(fold.Waiting.Select(p => p.PanelId)).IsEquivalentTo(new[] { PanelIds.Vitals, PanelIds.Map, PanelIds.Chat });
    }

    [Test]
    public async Task CombatWithEverythingHasAColumnOfVitalsOverTheMapAndListsChatAsCollapsed()
    {
        var fold = LayoutFold.Fold(BuiltInLayouts.Combat);

        // Row[ Column[ output, quick-bar ], Column[ vitals, map ] ]
        var root = (SplitNode)fold.Root!;
        await Assert.That(root.Direction).IsEqualTo(SplitDirection.Row);
        var side = (SplitNode)root.Children[1];
        await Assert.That(side.Direction).IsEqualTo(SplitDirection.Column);
        await Assert.That(side.Panes().Select(p => p.Id)).IsEquivalentTo(new[] { "panel:vitals", "panel:map" });
        await Assert.That(root.Panes().Select(p => p.Id))
            .IsEquivalentTo(new[] { "panel:output", "panel:quick-bar", "panel:vitals", "panel:map" });
        await Assert.That(fold.Collapsed.Select(p => p.PanelId)).IsEquivalentTo(new[] { PanelIds.Chat });
    }

    [Test]
    public async Task NarrowCombatPutsTheVitalsInAStripOnTop()
    {
        var narrow = BuiltInLayouts.ForWidth(BuiltInLayouts.Combat, narrow: true);
        await Assert.That(narrow.Id).IsEqualTo(BuiltInLayouts.CombatId);
        await Assert.That(narrow.IsBuiltIn).IsTrue();

        // Column[ vitals, Row[ Column[ output, quick-bar ], map ] ]
        var root = (SplitNode)LayoutFold.Fold(narrow).Root!;
        await Assert.That(root.Direction).IsEqualTo(SplitDirection.Column);
        await Assert.That(((PaneNode)root.Children[0]).Id).IsEqualTo("panel:vitals");
        await Assert.That(root.Sizes[0]).IsEqualTo(LayoutFold.DefaultShare(LayoutRegion.TopStrip)).Within(1e-9);
    }

    [Test]
    public async Task NarrowRoleplayPutsTheComposerUnderTheOutput()
    {
        var narrow = BuiltInLayouts.ForWidth(BuiltInLayouts.Roleplay, narrow: true);
        await Assert.That(narrow.Id).IsEqualTo(BuiltInLayouts.RoleplayId);

        // Row[ Column[ output+chat, composer ], who-here ]
        var root = (SplitNode)LayoutFold.Fold(narrow).Root!;
        await Assert.That(root.Direction).IsEqualTo(SplitDirection.Row);
        await Assert.That(root.Children[0].Panes().Select(p => p.Id)).IsEquivalentTo(new[] { "panel:output", "panel:composer" });
        await Assert.That(((PaneNode)root.Children[1]).Id).IsEqualTo("panel:who-here");
    }

    [Test]
    public async Task OnlyABuiltInHasANarrowArrangement()
    {
        await Assert.That(BuiltInLayouts.ForWidth(BuiltInLayouts.Combat, narrow: false)).IsSameReferenceAs(BuiltInLayouts.Combat);

        // A saved copy of a built-in is the player's, even under the built-in's id (a hand-edited file).
        var copy = BuiltInLayouts.Combat with { IsBuiltIn = false };
        await Assert.That(BuiltInLayouts.ForWidth(copy, narrow: true)).IsSameReferenceAs(copy);
        var library = new LayoutLibrary([]);
        var saved = library.SaveAsNew("Mine", BuiltInLayouts.Combat.Placements);
        await Assert.That(BuiltInLayouts.ForWidth(saved, narrow: true)).IsSameReferenceAs(saved);
    }

    [Test]
    public async Task PanelsInOneRegionShareItBySize()
    {
        var layout = new LayoutDefinition
        {
            Id = "x",
            Name = "X",
            Placements =
            [
                new() { PanelId = PanelIds.Output },
                new() { PanelId = PanelIds.GameData, Region = LayoutRegion.Side, Order = 2, Size = 3 },
                new() { PanelId = PanelIds.Map, Region = LayoutRegion.Side, Order = 1, Size = 1 },
            ],
            RegionShares = new Dictionary<LayoutRegion, double> { [LayoutRegion.Side] = 0.4 },
        };

        var root = (SplitNode)LayoutFold.Fold(layout).Root!;
        await Assert.That(root.Sizes[1]).IsEqualTo(0.4).Within(1e-9);

        var side = (SplitNode)root.Children[1];
        await Assert.That(side.Direction).IsEqualTo(SplitDirection.Column);
        await Assert.That(side.Panes().Select(p => p.Id)).IsEquivalentTo(new[] { "panel:map", "panel:game-data" });
        await Assert.That(side.Sizes[0]).IsEqualTo(0.25).Within(1e-9);
    }

    [Test]
    public async Task TabsDoNotChainAndAPanelPlacedTwiceGetsOnePane()
    {
        var layout = new LayoutDefinition
        {
            Id = "x",
            Name = "X",
            Placements =
            [
                new() { PanelId = PanelIds.Output },
                new() { PanelId = PanelIds.Map, Region = LayoutRegion.Side, TabOf = PanelIds.Chat },
                new() { PanelId = PanelIds.Chat, Region = LayoutRegion.Side, TabOf = PanelIds.Map },
                new() { PanelId = PanelIds.Output, Region = LayoutRegion.Bottom },
            ],
        };

        var panes = LayoutFold.Fold(layout).Root!.Panes().ToList();

        // The cycle breaks into two panes rather than losing both, and output is drawn once.
        await Assert.That(panes.Select(p => p.Id)).IsEquivalentTo(new[] { "panel:output", "panel:map", "panel:chat" });
        await Assert.That(panes.All(p => p.Tabs.Count == 1)).IsTrue();
    }
}
