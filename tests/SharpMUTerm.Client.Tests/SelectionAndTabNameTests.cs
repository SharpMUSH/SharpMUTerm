using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// One selected sidebar row (<see cref="SidebarModel.Plated(RailCharacter)"/>), and a strip of one character's tabs named
/// without the owner (<see cref="TabStripModel.OneOwner"/>).
/// </summary>
public class SelectionAndTabNameTests
{
    private static readonly RailWindow Main = new("main", "main:1", "⌥1", 0, false, false, Own: true);
    private static readonly RailWindow Chat = new("Chat", "spawn:1", "⌥2", 2, false, false);

    private static (SidebarModel Model, RailCharacter Corvid) Sidebar(string selected)
    {
        var corvid = new RailCharacter("Corvid", "Aetherfall.Corvid", Connected: true, Active: true, Unread: 2, [Main, Chat]);
        var world = new RailWorld("Aetherfall", "example", 4000, default, [corvid]);
        return (new SidebarModel([world], selected, Collapsed: false), corvid);
    }

    [Test]
    public async Task WithTheMainWindowInFrontOnlyTheCharacterIsPlated()
    {
        var (model, corvid) = Sidebar(Main.Id);

        await Assert.That(model.Plated(corvid)).IsTrue();
        await Assert.That(model.Plated(Main)).IsFalse();
        await Assert.That(model.Plated(Chat)).IsFalse();
    }

    [Test]
    public async Task WithAnotherWindowInFrontOnlyThatWindowIsPlated()
    {
        var (model, corvid) = Sidebar(Chat.Id);

        await Assert.That(model.Plated(corvid)).IsFalse();
        await Assert.That(model.Plated(Chat)).IsTrue();
    }

    private static TabChipModel Tab(string id, string name, string title, string? owner) =>
        new(id, name, title, Selected: false, Unread: 0, Draft: false, Foreign: false, Closable: false, owner);

    [Test]
    public async Task OneOwnersStripDropsThePrefix()
    {
        var tabs = TabStripModel.OneOwner([Tab("m", "Corvid", "Corvid", "A.C"), Tab("c", "Corvid · Chat", "Chat", "A.C")]);

        await Assert.That(tabs.Select(t => t.Name)).IsEquivalentTo(["Corvid", "Chat"]);
    }

    [Test]
    public async Task TwoOwnersOrALoneTabKeepThePrefix()
    {
        var mixed = TabStripModel.OneOwner([Tab("c", "Corvid · Chat", "Chat", "A.C"), Tab("r", "Rookery · Chat", "Chat", "A.R")]);
        var lone = TabStripModel.OneOwner([Tab("c", "Corvid · Chat", "Chat", "A.C")]);

        await Assert.That(mixed[0].Name).IsEqualTo("Corvid · Chat");
        await Assert.That(lone[0].Name).IsEqualTo("Corvid · Chat");
    }
}
