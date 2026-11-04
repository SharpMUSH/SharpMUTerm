using SharpMUTerm.Client.Palette;
using SharpMUTerm.Core.Commands;
using TUnit.Assertions.Enums;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// One opening of a host's native palette (<see cref="PaletteSession"/>): its rows are the shared model's over what
/// was captured when it opened, it runs at most one entry, and it says it closed exactly once.
/// </summary>
public class PaletteSessionTests
{
    private static readonly CommandItem[] Catalog =
    [
        new(CommandGroup.Terminal, "Newline", "term:newline", "Ctrl+⏎") { Key = "Ctrl+⏎" },
        new(CommandGroup.Layout, "Split right", "layout:split-right", "⌃B |") { Key = "⌃B |" },
        new(CommandGroup.Terminal, "Freeze pane", "term:freeze", "⌥F") { Key = "⌥F" },
    ];

    private static (PaletteSession Session, List<string> Ran, List<string> Closed) Make(List<CommandItem>? catalog = null)
    {
        var ran = new List<string>();
        var closed = new List<string>();
        var session = new PaletteSession(
            catalog ?? [.. Catalog], ["layout:split-right"], ["term:freeze"], "Aetherfall.Corvid", isDark: true, string.Empty,
            id => ran.Add(id), () => closed.Add("closed"));
        return (session, ran, closed);
    }

    [Test]
    public async Task TheRowsAreTheModelsOverTheCapture()
    {
        var catalog = Catalog.ToList();
        var (session, _, _) = Make(catalog);
        catalog.Clear();

        foreach (var query in new[] { string.Empty, "spl", "zz" })
        {
            var expected = CommandPaletteModel.Build(query, Catalog, ["layout:split-right"], ["term:freeze"]);
            await Assert.That(session.View(query).Rows).IsEquivalentTo(expected.Rows, CollectionOrdering.Matching);
        }

        await Assert.That(session.View(string.Empty).Entries.Select(e => e.Key)).Contains("⌃⏎");
        await Assert.That(session.Status(session.View("spl"))).IsEqualTo("1 of 3   acting on Aetherfall.Corvid");
    }

    [Test]
    public async Task RunClosesFirstThenRunsAndOnlyOnce()
    {
        var order = new List<string>();
        var session = new PaletteSession(Catalog, [], [], null, isDark: false, string.Empty, id => order.Add(id), () => order.Add("closed"));

        session.Run("term:freeze");
        session.Run("term:newline");
        session.Dismiss();

        await Assert.That(order).IsEquivalentTo(new[] { "closed", "term:freeze" }, CollectionOrdering.Matching);
        await Assert.That(session.IsEnded).IsTrue();
    }

    [Test]
    public async Task DismissClosesOnceAndRunsNothing()
    {
        var (session, ran, closed) = Make();

        session.Dismiss();
        session.Dismiss();
        session.Run("term:freeze");

        await Assert.That(ran).IsEmpty();
        await Assert.That(closed.Count).IsEqualTo(1);
    }
}
