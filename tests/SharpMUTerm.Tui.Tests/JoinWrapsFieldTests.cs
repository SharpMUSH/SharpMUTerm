using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>F5's <c>join wraps</c> row: one world field over <see cref="WorldDefinition.JoinWrappedLines"/>.</summary>
public class JoinWrapsFieldTests
{
    private static List<WorldDefinition> Worlds() =>
    [
        new() { Name = "Aetherfall", Host = "aether.example.org", Port = 4000 },
    ];

    [Test]
    public async Task TheRowWritesTheWorldsSettingBothWays()
    {
        var worlds = Worlds();
        var model = WorldsScreenRenderer.Model(worlds, [], 0, 0);
        var field = model.FieldAt(WorldsScreenRenderer.WorldsPane, 0, WorldsScreenRenderer.JoinWrapsField)!.Value;

        await Assert.That(field.Label).IsEqualTo("join wraps");
        await Assert.That(field.Get()).IsEqualTo("off");

        field.Set("join");
        await Assert.That(worlds[0].JoinWrappedLines).IsTrue();

        field.Set("off");
        await Assert.That(worlds[0].JoinWrappedLines).IsFalse();
    }

    [Test]
    public async Task TheDetailColumnSaysWhatItDoes()
    {
        var worlds = Worlds();
        var off = WorldsScreenRenderer.DetailColumn(worlds, [], 0, 0, ScreenPalette.Accent);
        worlds[0].JoinWrappedLines = true;
        var on = WorldsScreenRenderer.DetailColumn(worlds, [], 0, 0, ScreenPalette.Accent);

        await Assert.That(off.Any(l => l.Contains("join wraps") && l.Contains("rows as the server sent them"))).IsTrue();
        await Assert.That(on.Any(l => l.Contains("join wraps") && l.Contains("lines the server wrapped"))).IsTrue();
    }

    [Test]
    public async Task ACloneKeepsTheSetting()
    {
        var world = new WorldDefinition { Name = "W", JoinWrappedLines = true };

        await Assert.That(world.Clone().JoinWrappedLines).IsTrue();
    }
}
