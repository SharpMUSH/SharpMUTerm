using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Client.Tests;

/// <summary>Full screen and windowed full screen: offered only where a host has a window to size, titled for the way out.</summary>
public class WindowModeCommandTests
{
    private static IReadOnlyList<CommandItem> Catalog(WindowMode? mode) =>
        BuiltInCommands.Catalog(new Workspace(), [], null, new CommandContext(WindowMode: mode));

    [Test]
    public async Task AHostWithNoWindowOffersNeitherMode()
    {
        var ids = Catalog(null).Select(c => c.Id).ToArray();
        await Assert.That(ids).DoesNotContain(BuiltInCommands.FullScreenCommand);
        await Assert.That(ids).DoesNotContain(BuiltInCommands.WindowedFullScreenCommand);
    }

    [Test]
    public async Task AWindowOffersBoth_WithTheirKeys()
    {
        var catalog = Catalog(WindowMode.Windowed);
        var full = catalog.Single(c => c.Id == BuiltInCommands.FullScreenCommand);
        var windowed = catalog.Single(c => c.Id == BuiltInCommands.WindowedFullScreenCommand);
        await Assert.That(full.Title).IsEqualTo("Full screen");
        await Assert.That(full.Key).IsEqualTo("F11");
        await Assert.That(windowed.Title).IsEqualTo("Windowed full screen");
        await Assert.That(windowed.Key).IsEqualTo("⇧F11");
    }

    [Test]
    public async Task InAModeItsEntryIsTheWayOut()
    {
        await Assert.That(Catalog(WindowMode.FullScreen).Single(c => c.Id == BuiltInCommands.FullScreenCommand).Title)
            .IsEqualTo("Leave full screen");
        await Assert.That(Catalog(WindowMode.WindowedFullScreen).Single(c => c.Id == BuiltInCommands.WindowedFullScreenCommand).Title)
            .IsEqualTo("Leave windowed full screen");
        await Assert.That(Catalog(WindowMode.FullScreen).Single(c => c.Id == BuiltInCommands.WindowedFullScreenCommand).Title)
            .IsEqualTo("Windowed full screen");
    }

    [Test]
    public async Task TheOpeningSettingRoundTripsEveryMode()
    {
        var field = (ChoiceField)BuiltInSettings.Window.Fields.Single();
        var binding = (SettingBinding<string>)field.Binding!;
        foreach (var mode in Enum.GetValues<WindowMode>())
        {
            var config = new AppConfiguration { WindowStart = mode };
            var word = binding.Get(config);
            await Assert.That(field.Choices).Contains(word);
            await Assert.That(BuiltInSettings.WindowStartFor(word)).IsEqualTo(mode);
        }
    }

    [Test]
    public async Task TheWindowSectionIsNotAmongTheSectionsEveryHostHas()
    {
        await Assert.That(BuiltInSettings.All.Select(s => s.Id)).DoesNotContain(BuiltInSettings.Window.Id);
    }
}
