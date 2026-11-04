using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A host with its own palette (<see cref="ClientHost.NativePalette"/>) gets every way to ⌃P — the chord, the header,
/// the pointer menu's <em>More commands…</em> — and no cell popup; a host without one keeps the cell popup. The rows a
/// native palette is handed are the shared model's, and the entry it runs goes through the cell palette's own path.
/// </summary>
[NotInParallel]
public class NativePaletteRoutingTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class Presenter : INativePalette
    {
        public List<PaletteSession> Shown { get; } = [];

        public int Closed { get; private set; }

        public void Show(PaletteSession session) => Shown.Add(session);

        public void Close() => Closed++;
    }

    private static (SharpMUTermApp App, Presenter Native) App(bool native = true)
    {
        Console.SetIn(TextReader.Null);
        var presenter = new Presenter();
        var host = ClientHost.Headless(new HeadlessConsoleDriver(120, 34), Caps) with
        {
            NativePalette = native ? presenter : null,
        };
        return (new SharpMUTermApp(DemoScene.Build(), host), presenter);
    }

    private static readonly ConsoleKeyInfo CtrlP = new('\x10', ConsoleKey.P, false, false, true);

    private static IEnumerable<string> Shape(PaletteView view) =>
        view.Rows.Select(row => row switch
        {
            PaletteHeader header => "# " + header.Label,
            PaletteEntry entry => $"{entry.Item.Id}|{entry.Key}|{string.Join(',', entry.TitleMatches)}",
            _ => "?",
        });

    private static System.Drawing.Point CommandLineCell(SharpMUTermApp app)
    {
        for (var y = 33; y >= 0; y--)
        {
            for (var x = 0; x < 120; x++)
            {
                var cell = new System.Drawing.Point(x, y);
                if (app.TargetAt(cell).Kind == PointerKind.CommandLine)
                {
                    return cell;
                }
            }
        }

        throw new InvalidOperationException("no command line on screen");
    }

    [Test]
    public async Task CtrlPOpensTheHostsPaletteAndNoCellPopup()
    {
        var (app, native) = App();

        app.SimulateKey(CtrlP);

        await Assert.That(native.Shown.Count).IsEqualTo(1);
        await Assert.That(app.Palette.IsOpen).IsFalse().Because("no cell popup opens under a native one");
        await Assert.That(app.NativePaletteIsOpen).IsTrue();
        await Assert.That(app.MenuIsOpen).IsTrue();
        await Assert.That(app.BuildHeaderModel().MenuOpen).IsTrue().Because("the header's caret flips for either palette");
    }

    [Test]
    public async Task AHostWithNoPaletteOpensTheCellPopup()
    {
        var (app, native) = App(native: false);

        app.SimulateKey(CtrlP);

        await Assert.That(native.Shown).IsEmpty();
        await Assert.That(app.Palette.IsOpen).IsTrue();
    }

    [Test]
    public async Task TheSessionListsTheModelsRowsForEveryQuery()
    {
        var (app, native) = App();
        app.RunFromPalette("layout:split-right");
        app.SimulateKey(CtrlP);
        var session = native.Shown.Single();

        foreach (var query in new[] { string.Empty, "trig sel", "split", "zzzz" })
        {
            var expected = CommandPaletteModel.Build(
                query, app.BuildCatalog(), app.RecentCommandIds, app.SuggestedCommandIds);
            await Assert.That(Shape(session.View(query))).IsEquivalentTo(Shape(expected), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }

        await Assert.That(session.View(string.Empty).Rows[0]).IsEqualTo(new PaletteHeader(CommandPaletteModel.RecentLabel));
    }

    [Test]
    public async Task RunningAnEntryRecordsItAndDispatchesItAsTheCellPaletteDoes()
    {
        var (app, native) = App();
        app.SimulateKey(CtrlP);
        var session = native.Shown.Single();

        session.Run("layout:split-right");

        await Assert.That(app.RecentCommandIds[0]).IsEqualTo("layout:split-right");
        await Assert.That(app.PaneOutputRects().Count).IsEqualTo(2).Because("the split ran");
        await Assert.That(app.NativePaletteIsOpen).IsFalse();
        await Assert.That(app.BuildHeaderModel().MenuOpen).IsFalse();
    }

    [Test]
    public async Task EscapeInTheHostClosesItAndRunsNothing()
    {
        var (app, native) = App();
        app.SimulateKey(CtrlP);

        native.Shown.Single().Dismiss();

        await Assert.That(app.NativePaletteIsOpen).IsFalse();
        await Assert.That(app.RecentCommandIds).IsEmpty();
    }

    [Test]
    public async Task CtrlPAgainClosesTheHostsPalette()
    {
        var (app, native) = App();
        app.SimulateKey(CtrlP);
        app.SimulateKey(CtrlP);

        await Assert.That(native.Closed).IsEqualTo(1);
        await Assert.That(native.Shown.Single().IsEnded).IsTrue();
        await Assert.That(app.NativePaletteIsOpen).IsFalse();

        app.SimulateKey(CtrlP);
        await Assert.That(native.Shown.Count).IsEqualTo(2).Because("a third press opens it again");
    }

    [Test]
    public async Task TheHeaderOpensTheHostsPaletteAndNeverClosesIt()
    {
        var (app, native) = App();

        await Assert.That(app.ActivateChromeTarget(HeaderModel.PaletteTarget)).IsTrue();
        await Assert.That(app.ActivateChromeTarget(HeaderModel.PaletteTarget)).IsTrue();

        await Assert.That(native.Shown.Count).IsEqualTo(1);
        await Assert.That(native.Closed).IsEqualTo(0);
        await Assert.That(app.Palette.IsOpen).IsFalse();
    }

    [Test]
    public async Task ThePointerMenusMoreCommandsOpensThePalette()
    {
        foreach (var native in new[] { true, false })
        {
            var (app, presenter) = App(native);
            app.RenderWholeFrame();
            var input = CommandLineCell(app);
            var more = app.PointerMenu(input).Single(item => item.Label == SharpMUTermApp.MoreCommandsLabel);
            await Assert.That(more.Hint).IsEqualTo(HeaderModel.PaletteKey);

            more.Run();

            await Assert.That(presenter.Shown.Count).IsEqualTo(native ? 1 : 0);
            await Assert.That(app.Palette.IsOpen).IsEqualTo(!native);
        }
    }

    [Test]
    public async Task OpeningWithAQueryHandsItToEitherPalette()
    {
        var (app, native) = App();
        app.OpenMenu("trig sel");
        await Assert.That(native.Shown.Single().InitialQuery).IsEqualTo("trig sel");

        var (cells, _) = App(native: false);
        cells.OpenMenu("trig sel");
        await Assert.That(cells.Palette.IsOpen).IsTrue();
        await Assert.That(cells.Palette.View.Entries.Count).IsLessThan(cells.BuildCatalog().Count).Because("the cell popup filtered by it");
    }

    [Test]
    public async Task ItRefusesOverTheComposerAsTheCellPopupDoes()
    {
        var (app, native) = App();
        await Assert.That(app.DispatchCommand("term:compose")).IsTrue();
        await Assert.That(app.Composer.IsOpen).IsTrue();

        app.SimulateKey(CtrlP);
        app.ActivateChromeTarget(HeaderModel.PaletteTarget);

        await Assert.That(native.Shown).IsEmpty();
        await Assert.That(app.Palette.IsOpen).IsFalse();
    }
}
