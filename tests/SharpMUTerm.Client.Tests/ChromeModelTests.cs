using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Client.Tests;

/// <summary>The header and sidebar view-models, which the cell chrome and the window's native chrome are both drawn from.</summary>
public class ChromeModelTests
{
    private static readonly TerminalColor Teal = TerminalColor.FromRgb(0x00, 0xf5, 0xb7);

    private static IReadOnlyList<RailWorld> Tree(int unread = 3, bool unsent = false) =>
    [
        new RailWorld("Aetherfall", "aether.example", 4000, Teal,
        [
            new RailCharacter("Corvid", "Aetherfall.Corvid", Connected: true, Active: true, Unread: unread,
            [
                new RailWindow("main", "session:1", "⌥1", 0, unsent, false),
                new RailWindow("Chat", "spawn:2", "⌥2", unread, false, false),
            ]),
            new RailCharacter("Wren", "Aetherfall.Wren", Connected: false, Active: false, Unread: 0, [], Chord: "⌥J"),
        ]),
        new RailWorld("Empty Shore", "shore.example", 23, Teal, []),
    ];

    [Test]
    public async Task TheSidebarsRowsAreCoresRowProjectionOfTheSameTree()
    {
        var model = new SidebarModel(Tree(), "spawn:2", false);
        await Assert.That(model.Rows()).IsEquivalentTo(RailModel.Build(model.Worlds));
        await Assert.That(model.Rows().Count(r => r.Kind == RailRowKind.Window)).IsEqualTo(2);
    }

    [Test]
    public async Task TwoSidebarsOverTheSameStateAreEqualAndAnyChangeIsNot()
    {
        await Assert.That(new SidebarModel(Tree(), "spawn:2", false)).IsEqualTo(new SidebarModel(Tree(), "spawn:2", false));
        await Assert.That(new SidebarModel(Tree(), "spawn:2", false).GetHashCode())
            .IsEqualTo(new SidebarModel(Tree(), "spawn:2", false).GetHashCode());
        await Assert.That(new SidebarModel(Tree(unread: 4), "spawn:2", false)).IsNotEqualTo(new SidebarModel(Tree(), "spawn:2", false));
        await Assert.That(new SidebarModel(Tree(unsent: true), "spawn:2", false)).IsNotEqualTo(new SidebarModel(Tree(), "spawn:2", false));
        await Assert.That(new SidebarModel(Tree(), "session:1", false)).IsNotEqualTo(new SidebarModel(Tree(), "spawn:2", false));
        await Assert.That(new SidebarModel(Tree(), "spawn:2", true)).IsNotEqualTo(new SidebarModel(Tree(), "spawn:2", false));
    }

    [Test]
    public async Task EveryTargetTheSidebarOffersIsOneTheCellRailCarriesOrTheAddButton()
    {
        var model = new SidebarModel(Tree(), "spawn:2", false);
        var rowTargets = model.Rows().Select(r => r.Target).OfType<string>().ToHashSet();
        foreach (var target in model.Targets().Where(t => t != SidebarModel.AddGameTarget))
        {
            await Assert.That(rowTargets).Contains(target).Because(target);
        }

        await Assert.That(model.Targets()).Contains(CommandIds.Window("spawn:2"));
        await Assert.That(model.Targets()).Contains(CommandIds.Character("Aetherfall.Wren"));
        await Assert.That(model.Targets()).Contains(RailModel.NoCharactersTarget("Empty Shore"));
        await Assert.That(model.IsSelected(model.Worlds[0].Characters[0].Windows[1])).IsTrue();
    }

    [Test]
    public async Task TheHeaderWordsItsCountsAndLogTheWayTheCellRowDoes()
    {
        var model = new HeaderModel(false, new HeaderWorld("Aetherfall", Teal),
            new HeaderCharacter("Corvid", "Aetherfall.Corvid", CharacterPresence.Online, "aether.example", 4000, true, "char:Aetherfall.Corvid"),
            2, 3, true, LogFormat.Html, false);

        await Assert.That(model.ConnectedText).IsEqualTo("2/3 characters");
        await Assert.That(model.LogText).IsEqualTo("LOG html");
        await Assert.That(model.Character!.Detail("Aetherfall")).IsEqualTo("Aetherfall · TLS");
        await Assert.That((model with { Character = model.Character with { Tls = false } }).Character!.Detail("Aetherfall")).IsEqualTo("Aetherfall");
        await Assert.That(HeaderModel.Empty.ConnectedText).IsEqualTo(string.Empty);
        await Assert.That(HeaderModel.Empty.LogText).IsEqualTo("LOG off");
        await Assert.That(model).IsEqualTo(model with { });
    }

    [Test]
    public async Task PresenceFollowsTheSessionsState()
    {
        await Assert.That(PresenceOf.State(ConnectionState.Connected)).IsEqualTo(CharacterPresence.Online);
        await Assert.That(PresenceOf.State(ConnectionState.Connecting)).IsEqualTo(CharacterPresence.Connecting);
        await Assert.That(PresenceOf.State(ConnectionState.Faulted)).IsEqualTo(CharacterPresence.Offline);
        await Assert.That(PresenceOf.State(null)).IsEqualTo(CharacterPresence.Offline);
    }

    [Test]
    public async Task ChordsAreSpeltInWordsForAUiFont()
    {
        await Assert.That(KeyGlyphs.Words("⌥1")).IsEqualTo("Alt+1");
        await Assert.That(KeyGlyphs.Words("⌃P")).IsEqualTo("Ctrl+P");
        await Assert.That(KeyGlyphs.Words("⌥⇧→")).IsEqualTo("Alt+Shift+→");
        await Assert.That(KeyGlyphs.Words("F4")).IsEqualTo("F4");
        await Assert.That(KeyGlyphs.Normalize(KeyGlyphs.Words("⌥J"))).IsEqualTo("⌥J");
    }
}
