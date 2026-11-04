using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// ⌥C puts a cursor on the connection sidebar: ↑↓ Home End walk it, ⏎ goes where a click on the row goes, Esc and
/// ⌥C hand the keys back, and any other key does too and then does what it does on the command line. The cursor is
/// client state on the shared <see cref="SidebarModel"/>, so the cell rail draws it as a band and the window's
/// sidebar as a ring. Also the row menu both hosts open on a right click: registry commands only.
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class RailKeysTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static ConsoleKeyInfo Key(ConsoleKey key, char c = '\0', bool alt = false) => new(c, key, false, alt, false);

    private static readonly ConsoleKeyInfo AltC = Key(ConsoleKey.C, 'c', alt: true);

    private static async Task<SharpMUTermApp> App(string? view = "characters")
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(new HeadlessConsoleDriver(120, 34), Caps));
        app.RenderSnapshot(view);
        await Task.CompletedTask;
        return app;
    }

    [Test]
    public async Task AltCPutsTheCursorOnTheWindowInFrontAndAltCTakesItOff()
    {
        await using var app = await App();
        var front = SidebarModel.KeyOf(RailRowKind.Window, CommandIds.Window(app.ActiveWindowId()));

        app.SimulateKey(AltC);
        await Assert.That(app.RailKeysActive).IsTrue();
        await Assert.That(app.RailCursor).IsEqualTo(front);
        await Assert.That(app.BuildChromeOverlay().Sidebar.Cursor).IsEqualTo(front).Because("the window's sidebar reads the same model");

        app.SimulateKey(AltC);
        await Assert.That(app.RailKeysActive).IsFalse();
        await Assert.That(app.BuildChromeOverlay().Sidebar.Cursor).IsNull();
    }

    /// <summary>While the sidebar holds the keys the command line is drawn idle — ⏎ goes to a row, not to the game.</summary>
    [Test]
    public async Task TheCommandLineIsIdleWhileTheSidebarHoldsTheKeys()
    {
        await using var app = await App();
        var (armed, idle) = app.InputBandColors;
        await Assert.That(app.PrimaryBandColor).IsEqualTo(armed);

        app.SimulateKey(AltC);
        await Assert.That(app.PrimaryBandColor).IsEqualTo(idle);

        app.SimulateKey(Key(ConsoleKey.Escape));
        await Assert.That(app.PrimaryBandColor).IsEqualTo(armed).Because("handing the keys back arms the bar again");
    }

    [Test]
    public async Task TheArrowsWalkTheRowsAClickWouldReachAndStopAtTheEnds()
    {
        await using var app = await App();
        var rows = app.BuildChromeOverlay().Sidebar.NavigableRows().Select(SidebarModel.KeyOf).ToList();
        app.SimulateKey(AltC);

        app.SimulateKey(Key(ConsoleKey.Home));
        await Assert.That(app.RailCursor).IsEqualTo(rows[0]);
        app.SimulateKey(Key(ConsoleKey.UpArrow));
        await Assert.That(app.RailCursor).IsEqualTo(rows[0]).Because("clamped, not wrapped");
        app.SimulateKey(Key(ConsoleKey.DownArrow));
        await Assert.That(app.RailCursor).IsEqualTo(rows[1]);
        app.SimulateKey(Key(ConsoleKey.End));
        await Assert.That(app.RailCursor).IsEqualTo(rows[^1]);
        app.SimulateKey(Key(ConsoleKey.DownArrow));
        await Assert.That(app.RailCursor).IsEqualTo(rows[^1]);
    }

    [Test]
    public async Task EnterGoesWhereAClickOnTheRowGoesAndHandsTheKeysBack()
    {
        await using var app = await App();
        var before = app.ActiveWindowId();
        var target = app.BuildChromeOverlay().Sidebar.NavigableRows()
            .Last(r => r.Kind == RailRowKind.Window && r.Target != CommandIds.Window(before) && !r.Closed).Target!;

        app.SimulateKey(AltC);
        app.SimulateKey(Key(ConsoleKey.End));
        while (app.RailCursor != SidebarModel.KeyOf(RailRowKind.Window, target))
        {
            app.SimulateKey(Key(ConsoleKey.UpArrow));
        }

        app.SimulateKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(app.RailKeysActive).IsFalse();
        await Assert.That(CommandIds.Window(app.ActiveWindowId())).IsEqualTo(target);
    }

    [Test]
    public async Task EscReturnsAndATypedKeyReturnsAndIsTyped()
    {
        await using var app = await App();
        var draft = app.PrimaryInputText;
        app.SimulateKey(AltC);
        app.SimulateKey(Key(ConsoleKey.Escape, '\u001b'));
        await Assert.That(app.RailKeysActive).IsFalse();
        await Assert.That(app.PrimaryInputText).IsEqualTo(draft);

        app.SimulateKey(AltC);
        app.SimulateKey(Key(ConsoleKey.L, 'l'));
        await Assert.That(app.RailKeysActive).IsFalse();
        await Assert.That(app.PrimaryInputText).IsEqualTo(draft + "l").Because("a reader who starts typing is never stuck");
    }

    /// <summary>The cell rail draws the cursor as a band on its row, padded to the rail's width: no cell is spent.</summary>
    [Test]
    public async Task TheCellRailBandsTheCursorRowAndMovesNoPane()
    {
        await using var app = await App();
        var rects = app.PaneOutputRects().ToDictionary(p => p.Key, p => p.Value);
        var widths = app.RailLines.Select(SharpMUTermApp.MarkupWidth).ToList();

        app.SimulateKey(AltC);

        var lines = app.RailLines;
        await Assert.That(lines.Count(l => System.Text.RegularExpressions.Regex.IsMatch(l, "^\\[#[0-9a-f]{6} on #"))).IsEqualTo(1);
        await Assert.That(lines.Select(SharpMUTermApp.MarkupWidth).Max()).IsEqualTo(widths.Max());
        await Assert.That(app.PaneOutputRects()).IsEquivalentTo(rects);
    }

    [Test]
    public async Task TheRowMenuOffersOnlyRegistryCommandsWithTheirKeys()
    {
        await using var app = await App();
        foreach (var row in app.BuildChromeOverlay().Sidebar.NavigableRows())
        {
            var menu = app.SidebarRowMenu(SidebarModel.KeyOf(row));
            foreach (var id in menu.Commands())
            {
                await Assert.That(app.Contributions.Commands.Resolve(id) is not null || id == row.Target).IsTrue().Because(id);
            }
        }

        var character = app.BuildChromeOverlay().Sidebar.NavigableRows().First(r => r.Kind == RailRowKind.Character);
        var labels = app.SidebarRowMenu(SidebarModel.KeyOf(character)).Entries.Select(e => e.Label).ToList();
        await Assert.That(labels.Any(l => l.Contains("in F5", StringComparison.Ordinal))).IsTrue();
        await Assert.That(labels.Any(l => l is "Connect" or "Disconnect")).IsTrue();
    }

    [Test]
    public async Task AnEntryTheMenuNeverOfferedIsRefused()
    {
        await using var app = await App();
        var window = app.BuildChromeOverlay().Sidebar.NavigableRows().First(r => r.Kind == RailRowKind.Window);
        app.SidebarRowMenu(SidebarModel.KeyOf(window));

        await Assert.That(app.RunSidebarMenuEntry("app:quit")).IsFalse();
        await Assert.That(app.LastNotice).Contains("never offered");
    }

    [Test]
    public async Task AnEntryBringsItsRowForwardFirst()
    {
        await using var app = await App();
        var settings = app.BuildChromeOverlay().Sidebar.NavigableRows().First(r => r.Kind == RailRowKind.Character);
        var menu = app.SidebarRowMenu(SidebarModel.KeyOf(settings));
        var edit = menu.Entries.First(e => e.Label.Contains("in F5", StringComparison.Ordinal)).Command!;

        await Assert.That(app.RunSidebarMenuEntry(edit)).IsTrue();
        await Assert.That(app.OpenSettingsKey).IsNotNull();
    }
}
