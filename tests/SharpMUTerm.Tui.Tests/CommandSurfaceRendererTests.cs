using SharpMUTerm.Client.Palette;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

public class CommandSurfaceRendererTests
{
    private static readonly CommandItem[] Items =
    {
        new(CommandGroup.Layout, "Zoom pane", "layout:zoom", "⌃B z") { Key = "⌃B z" },
        new(CommandGroup.GoTo, "Switch to Rookery", "char:k2", "Aetherfall · offline") { Detail = "Aetherfall · offline" },
        new(CommandGroup.GoTo, "Go to #public", "win:2", "⌥2 · Corvid · 3 unread") { Key = "⌥2", Detail = "Corvid · 3 unread" },
        new(CommandGroup.Terminal, "Start logging", "term:log-on"),
        new(CommandGroup.Terminal, "Insert a newline in the command line", "term:newline", "Alt+⏎") { Key = "Alt+⏎" },
        new(CommandGroup.Settings, "Open Worlds & Characters", "screen:worlds", "F5") { Key = "F5" },
    };

    [Test]
    public async Task Render_DrawsTheSettingsGroup_WithEachScreensKey()
    {
        var lines = CommandSurfaceRenderer.Render(CommandPaletteModel.Build("", Items), selected: -1, context: null);

        await Assert.That(lines.Any(l => l.Contains("├ SETTINGS"))).IsTrue();
        await Assert.That(lines.Any(l => l.Contains("Open Worlds & Characters") && l.Contains("F5"))).IsTrue();
    }

    [Test]
    public async Task Render_EmitsGroupHeadersAndHighlightsSelected()
    {
        var lines = CommandSurfaceRenderer.Render(CommandPaletteModel.Build("", Items), selected: 0, context: "Corvid");

        await Assert.That(lines[0]).Contains("acting on Corvid");
        await Assert.That(lines.Any(l => l.Contains("├ GO TO"))).IsTrue();
        await Assert.That(lines.Any(l => l.Contains("├ WINDOWS & PANES"))).IsTrue();
        await Assert.That(lines.Any(l => l.Contains("on #00f5b7") && l.Contains("Switch to Rookery"))).IsTrue();
    }

    [Test]
    public async Task Render_NoMatches_SaysSo()
    {
        var lines = CommandSurfaceRenderer.Render(CommandPaletteModel.Build("zzzz", Items), -1, null);
        await Assert.That(lines.Any(l => l.Contains("no matches"))).IsTrue();
    }

    /// <summary>
    /// The key column: every key ends on the same column, which is one cell short of the row's edge, and the
    /// host's spelt-out newline chord is drawn in the same glyphs as the rest.
    /// </summary>
    [Test]
    public async Task EveryKeyEndsOnTheSameColumn_InOneGlyphVocabulary()
    {
        var view = CommandPaletteModel.Build("", Items);
        var lines = CommandSurfaceRenderer.Render(view, selected: -1, context: null, width: 70);
        var rows = lines.Where(l => !l.StartsWith("[dim]", StringComparison.Ordinal)).ToList();

        foreach (var row in rows)
        {
            await Assert.That(MarkupText.VisibleLength(row)).IsEqualTo(70);
        }

        var plain = rows.Select(Strip).ToList();
        await Assert.That(plain.Single(p => p.Contains("Zoom pane"))).EndsWith("⌃B z ");
        await Assert.That(plain.Single(p => p.Contains("newline"))).EndsWith("⌥⏎ ");
        await Assert.That(string.Join("\n", plain)).DoesNotContain("Alt+");
    }

    /// <summary>A surface narrower than its rows elides the title and the detail, never the key.</summary>
    [Test]
    public async Task ANarrowSurfaceElidesTitlesButNeverKeys()
    {
        var view = CommandPaletteModel.Build("", Items);
        var lines = CommandSurfaceRenderer.Render(view, selected: -1, context: null, width: 24);
        var newline = Strip(lines.Single(l => l.Contains("Insert")));

        await Assert.That(newline.Length).IsEqualTo(24);
        await Assert.That(newline).Contains("…");
        await Assert.That(newline).EndsWith("⌥⏎ ");
    }

    [Test]
    public async Task MatchedCharactersAreHighlighted()
    {
        var view = CommandPaletteModel.Build("zp", Items);
        var lines = CommandSurfaceRenderer.Render(view, selected: -1, context: null);
        var zoom = lines.Single(l => l.Contains("oom"));

        await Assert.That(zoom).Contains("[bold #00f5b7]Z[/]");
        await Assert.That(zoom).Contains("[bold #00f5b7]p[/]");
    }

    private static string Strip(string markup) =>
        System.Text.RegularExpressions.Regex.Replace(
            markup.Replace("[[", "\u0001").Replace("]]", "\u0002"), @"\[[^\]]*\]", string.Empty)
            .Replace("\u0001", "[").Replace("\u0002", "]");
}
