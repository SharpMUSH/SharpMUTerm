using System.Drawing;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Parsing;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The header and the rail as data (<see cref="HeaderModel"/>, <see cref="SidebarModel"/>): the cell chrome is drawn from
/// the same models a host is handed, the host is told where the header and the rail are, and a click it hands back
/// goes where the same click on the cells goes — and nowhere the client did not offer.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class NativeChromeModelTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class ChromePresenter : IPointerPresenter
    {
        public List<ChromeOverlay> Published { get; } = [];

        public void Hover(PointerTarget target)
        {
        }

        public void ShowMenu(Point cell, IReadOnlyList<PointerMenuItem> items)
        {
        }

        public bool DrawsMenus => true;

        public void Scrollbars(IReadOnlyList<PaneScrollbar> bars)
        {
        }

        public void Chrome(ChromeOverlay chrome) => Published.Add(chrome);
    }

    private static (SharpMUTermApp App, ChromePresenter Pointer) App(string? view = null)
    {
        Console.SetIn(TextReader.Null);
        var pointer = new ChromePresenter();
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, _ => { }) with { Pointer = pointer };
        var app = new SharpMUTermApp(DemoScene.Build(), host);
        app.RenderSnapshot(view);
        app.RenderNextFrame();
        return (app, pointer);
    }

    [Test]
    public async Task TheCellHeaderSaysWhatTheHeaderModelSays()
    {
        var (app, _) = App("connections");
        await using var _app = app;
        var model = app.BuildHeaderModel();
        await Assert.That(model.World).IsNotNull();
        await Assert.That(model.Character).IsNotNull();
        await Assert.That(app.HeaderText).Contains(model.World!.Name);
        await Assert.That(app.HeaderText).Contains(model.Character!.Name);
        await Assert.That(app.HeaderText).Contains(model.ConnectedText);
        await Assert.That(app.HeaderText).Contains(model.LogText);
        await Assert.That(app.HeaderText).Contains($"[link={HeaderModel.PaletteTarget}]");
    }

    [Test]
    public async Task TheCellRailCarriesExactlyTheSidebarModelsTargets()
    {
        var (app, _) = App("characters");
        await using var _app = app;
        var sidebar = app.BuildSidebar();
        var rowTargets = sidebar.Rows().Select(r => r.Target).OfType<string>().Distinct().ToArray();
        var linked = string.Join("\n", app.RailLines);
        foreach (var target in rowTargets)
        {
            await Assert.That(linked).Contains(LinkUrl.Escape(target)).Because(target);
        }

        await Assert.That(app.RailLines.Count).IsEqualTo(sidebar.Rows().Count);
        await Assert.That(sidebar.SelectedWindowId).IsEqualTo(app.ActiveWindowId());
    }

    [Test]
    public async Task TheHostIsToldWhereTheHeaderAndTheRailAre()
    {
        var (app, pointer) = App("characters");
        await using var _app = app;
        var chrome = pointer.Published.Last();
        await Assert.That(chrome.Shown).IsTrue();
        await Assert.That(chrome.Header).IsEqualTo(new PaneRect(0, 0, 120, 1));
        await Assert.That(chrome.Rail.X).IsEqualTo(0);
        await Assert.That(chrome.Rail.Y).IsEqualTo(1);
        await Assert.That(chrome.Rail.Width).IsEqualTo(app.RailColumnWidth);
        await Assert.That(chrome.Rail.Height).IsGreaterThan(10);
        await Assert.That(chrome.Sidebar).IsEqualTo(app.BuildSidebar());
        await Assert.That(chrome.HeaderModel).IsEqualTo(app.BuildHeaderModel());

        // Nothing changed, nothing said: a frame that moved nothing does not re-publish.
        var count = pointer.Published.Count;
        app.RenderNextFrame();
        await Assert.That(pointer.Published.Count).IsEqualTo(count);
    }

    [Test]
    public async Task TheSearchBoxOpensTheCommandSurfaceAndTheChromeStandsDownUnderIt()
    {
        var (app, pointer) = App();
        await using var _app = app;
        await Assert.That(app.MenuIsOpen).IsFalse();
        pointer.Published.Last().Activate(HeaderModel.PaletteTarget);
        await Assert.That(app.MenuIsOpen).IsTrue();

        // A second click on the box does not close it: a search box is for starting to type.
        await Assert.That(app.ActivateChromeTarget(HeaderModel.PaletteTarget)).IsTrue();
        await Assert.That(app.MenuIsOpen).IsTrue();

        app.RenderNextFrame();
        await Assert.That(pointer.Published.Last().Shown).IsFalse().Because("the command surface is drawn over the cells the chrome would cover");
    }

    [Test]
    public async Task ASidebarClickGoesWhereTheRailsOwnClickGoes()
    {
        var (app, pointer) = App("characters");
        await using var _app = app;
        var chrome = pointer.Published.Last();
        var other = chrome.Sidebar.Worlds.SelectMany(w => w.Characters).First(c => !c.Active);
        await Assert.That(app.ActivateChromeTarget(SidebarModel.TargetOf(other))).IsTrue();
        await Assert.That(app.BuildSidebar().Worlds.SelectMany(w => w.Characters).Single(c => c.Active).SessionKey)
            .IsEqualTo(other.SessionKey);
    }

    [Test]
    [Arguments("characters")]
    [Arguments("layout-roleplay")]
    [Arguments(null)]
    public async Task TheHeadersPresenceDotAndTheRailsDotAreOneAnswer(string? view)
    {
        var (app, _) = App(view);
        await using var _app = app;
        var header = app.BuildHeaderModel().Character!;
        var row = app.BuildSidebar().Worlds.SelectMany(w => w.Characters).Single(c => c.SessionKey == header.SessionKey);
        await Assert.That(header.Presence == CharacterPresence.Online).IsEqualTo(row.Connected);
    }

    [Test]
    public async Task TheChromeStandsDownUnderTheQuitPrompt()
    {
        // The connections scene leaves the quit prompt open over the main window.
        var (app, pointer) = App("connections");
        await using var _app = app;
        await Assert.That(pointer.Published.Last().Shown).IsFalse();
    }

    [Test]
    public async Task ATargetTheChromeNeverOfferedIsRefused()
    {
        var (app, _) = App("connections");
        await using var _app = app;
        var before = app.ActiveWindowId();
        await Assert.That(app.ActivateChromeTarget("mux:send:@shutdown")).IsFalse();
        await Assert.That(app.ActivateChromeTarget(CommandIds.Window("no-such-window"))).IsFalse();
        await Assert.That(app.ActivateChromeTarget("layout:close")).IsFalse();
        await Assert.That(app.ActiveWindowId()).IsEqualTo(before);
    }
}
