using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui.Panels;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A host that draws strips (<see cref="HostChrome.Strips"/>, the window) gives the Combat layout's quick bar the rows its
/// buttons need and draws a collapsed panel as a bar of its own at the foot of its column, which a click expands. The
/// terminal keeps the one-row bar and the background tab. Rows only: the pane rectangles are what is arranged.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class CombatStripTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App(HostChrome chrome, out string frame)
    {
        Console.SetIn(TextReader.Null);
        var host = ClientHost.Headless(new HeadlessConsoleDriver(140, 40), Caps) with { Chrome = chrome };
        var app = new SharpMUTermApp(DemoScene.Build(), host);
        frame = app.RenderSnapshot("layout-combat");
        app.RenderNextFrame();
        return app;
    }

    private static string? PaneHolding(SharpMUTermApp app, string panelId) =>
        app.PaneNodes.FirstOrDefault(p => p.Tabs.Any(t => t.StartsWith("panel:" + panelId + ":", StringComparison.Ordinal)))?.Id;

    [Test]
    public async Task TheWindowsQuickBarIsAStripTallEnoughForButtonsAndTheTerminalsIsOneRow()
    {
        await using var window = App(HostChrome.Desktop, out _);
        await using var terminal = App(HostChrome.None, out _);
        var windowBar = PaneHolding(window, PanelIds.QuickBar)!;
        var terminalBar = PaneHolding(terminal, PanelIds.QuickBar)!;

        await Assert.That(window.PaneOutputRects()[windowBar].Height).IsEqualTo(PanelSizing.QuickBarStripRows);
        await Assert.That(terminal.PaneOutputRects()[terminalBar].Height).IsEqualTo(1);
    }

    [Test]
    public async Task ACollapsedPanelIsABarAtTheFootOfItsColumnInTheWindowAndATabInTheTerminal()
    {
        await using var window = App(HostChrome.Desktop, out _);
        await using var terminal = App(HostChrome.None, out _);

        var bar = window.PaneNodes.Single(p => LayoutRealiser.CollapsedPanelOf(p.Id) == PanelIds.Chat);
        await Assert.That(window.IsCollapsedBarPane(bar.Id)).IsTrue();
        await Assert.That(window.PaneOutputRects()[bar.Id].Height).IsEqualTo(window.CollapsedBarRows);

        // Its column runs down to the status row: nothing under the bar.
        var map = window.PaneNodes.Single(p => p.Tabs.Any(t => t.StartsWith("map:", StringComparison.Ordinal)));
        var rects = window.PaneOutputRects();
        await Assert.That(rects[bar.Id].Y).IsGreaterThan(rects[map.Id].Y);
        await Assert.That(rects[bar.Id].X).IsEqualTo(rects[map.Id].X);
        var bottom = 40;
        await Assert.That(rects[bar.Id].Y + rects[bar.Id].Height).IsEqualTo(bottom).Because(string.Join("; ", rects.Select(r => $"{r.Key}={r.Value}")));

        var model = window.CollapsedBarModel(bar.Id);
        await Assert.That(model).IsNotNull();
        await Assert.That(model!.Title).IsEqualTo("Chat");
        await Assert.That(model.Note).IsEqualTo("collapsed in combat");

        // The terminal: no such pane, the chat window a background tab of the map's pane as before.
        await Assert.That(terminal.PaneNodes.Any(p => LayoutRealiser.CollapsedPanelOf(p.Id) is not null)).IsFalse();
    }

    [Test]
    public async Task TheBarsLinkExpandsItIntoAPaneThatSharesItsColumn()
    {
        await using var window = App(HostChrome.Desktop, out _);
        var bar = window.PaneNodes.Single(p => LayoutRealiser.CollapsedPanelOf(p.Id) is not null);
        var markup = window.CollapsedBarModel(bar.Id)!;
        await Assert.That(CollapsedBarRenderer.Render(markup, 40, "#00f5b7")).Contains(CollapsedBarRenderer.ExpandLink);

        window.ExpandCollapsedBar(bar.Id);
        window.RenderNextFrame();

        await Assert.That(window.IsCollapsedBarPane(bar.Id)).IsFalse();
        await Assert.That(window.PaneOutputRects()[bar.Id].Height).IsGreaterThan(window.CollapsedBarRows);
        await Assert.That(window.FocusedPaneId).IsEqualTo(bar.Id);
    }

    [Test]
    public async Task ADangerBindingIsDrawnInTheWarningInkAndOthersInTheAccent()
    {
        var model = new QuickBarModel([
            new QuickBarItem("F1", "F1", "kick", true),
            new QuickBarItem("⌃F12", "Ctrl+F12", "flee", true, Danger: true),
        ]);
        var row = QuickBarRenderer.Render(model, 80, "#00f5b7", "#ff6b6b");
        await Assert.That(row).Contains("[#00f5b7]F1[/] kick");
        await Assert.That(row).Contains("[#ff6b6b]⌃F12[/] [#ff6b6b]flee[/]");
    }
}
