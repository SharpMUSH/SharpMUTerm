using SharpConsoleUI.Drivers;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Selecting a pane's output with the mouse and copying it.
/// <para>
/// The terminal's own selection cannot do this job: it selects a terminal <em>row</em>, so on a vertical
/// split a drag returns the left pane's text, the divider and the right pane's unrelated output
/// concatenated — and a pane is narrower than the row, so a logical line wraps and comes back with hard
/// newlines injected at the wrap points. Both are <c>UrlDetector</c>'s problem one layer over: the
/// decision has to be made where the pane's line is known to end.
/// </para>
/// <para>
/// Every gesture here goes through the control's real <c>ProcessMouseEvent</c>, because the framework
/// only registers its driver-mouse handler inside <c>Run()</c> — which no test calls. That is the same
/// limitation <c>SimulatePaneClick</c> documents, and the reason a drag needs a seam of its own.
/// </para>
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class PaneSelectionTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp Demo()
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(Width, Height));
    }

    /// <summary>The main window, rendered, with a known line of output to drag across.</summary>
    private static SharpMUTermApp Rendered()
    {
        var app = Demo();
        app.RenderSnapshot();
        return app;
    }

    /// <summary>
    /// The same, with somewhere for a copy to land. The writer is caller-supplied and null by default —
    /// the <c>save</c>/<c>logRoot</c>/browser-launcher family — so a test that does not ask for one
    /// provably leaves the real system clipboard alone.
    /// </summary>
    private static (SharpMUTermApp App, List<string> Copied) WithClipboard()
    {
        Console.SetIn(TextReader.Null);
        var copied = new List<string>();
        var app = new SharpMUTermApp(
            DemoScene.Build(), Headless, new HeadlessConsoleDriver(Width, Height), clipboard: copied.Add);
        app.RenderSnapshot();
        return (app, copied);
    }

    private static ConsoleKeyInfo CtrlC =>
        new('\0', ConsoleKey.C, shift: false, alt: false, control: true);

    [Test]
    public async Task CtrlCCopiesWhatWasSelected()
    {
        var (app, copied) = WithClipboard();
        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 25, 1);

        app.SimulateKey(CtrlC);

        await Assert.That(copied).HasSingleItem();
        await Assert.That(copied[0]).IsEqualTo(app.PaneSelection(SharpMUTermApp.MainWindowId));
    }

    /// <summary>Nothing selected is not an error, but it is not silence either.</summary>
    [Test]
    public async Task CtrlCWithNothingSelectedSaysSo()
    {
        var (app, copied) = WithClipboard();

        app.SimulateKey(CtrlC);

        await Assert.That(copied).IsEmpty();
        await Assert.That(app.StatusMarkup).Contains("nothing selected");
    }

    /// <summary>
    /// The family rule, asserted rather than assumed: an app given no writer copies nowhere and says so.
    /// A test that quietly reached the real clipboard would replace whatever the developer had on it.
    /// </summary>
    [Test]
    public async Task AnAppWithNoClipboardWriterCopiesNothingAndSaysSo()
    {
        var app = Rendered();
        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 25, 1);

        app.SimulateKey(CtrlC);

        await Assert.That(app.StatusMarkup).Contains("no clipboard");
    }

    /// <summary>
    /// A drag in a pane the keyboard is <em>not</em> aimed at still copies. Pane selection is moved by
    /// ⌃arrows, ⌃O and a tab click — never by a press in a pane's body — so a copy resolved through
    /// <c>ActiveWindowId</c> looked in the wrong pane and reported nothing selected, which is the shape of
    /// a feature that does not work. The framework's <c>SelectionManager</c> already arbitrates one
    /// selection per window; asking it is asking the thing that knows.
    /// </summary>
    [Test]
    public async Task ADragInAPaneThatDoesNotHoldTheFocusIsStillWhatGetsCopied()
    {
        var (app, copied) = WithClipboard();
        app.RenderSnapshot("split");

        // By window, not by pane: the two are separate id namespaces and the drag seam takes a window.
        var elsewhere = app.PaneWindows().Values.First(id => id != app.ActiveWindowId());

        app.SimulatePaneDrag(elsewhere, 0, 0, 20, 1);
        app.SimulateKey(CtrlC);

        await Assert.That(app.ActiveWindowId()).IsNotEqualTo(elsewhere);
        await Assert.That(copied).HasSingleItem();
        await Assert.That(copied[0]).IsEqualTo(app.PaneSelection(elsewhere));
    }

    /// <summary>
    /// Freezing rebuilds the pane into a pinned half and a live half and re-feeds both, so a selection
    /// anchored to the rows before the split describes a grid that no longer exists. It is a second
    /// re-feed seam beside <c>RepaintPane</c>, which is why the clearing lives in <c>FeedRange</c> — the
    /// one function that actually replaces a control's content — rather than at the call sites.
    /// </summary>
    /// <summary>
    /// Freeze is a second re-feed seam beside <c>RepaintPane</c> — it rebuilds the pane into a pinned half
    /// and a live half and feeds both — and unfreezing pours the whole buffer back. That round trip is the
    /// one with teeth: freezing alone leaves the live control <em>empty</em>, so a stale anchor yields
    /// nothing and the client refuses for the wrong reason, while after an unfreeze the rows exist again
    /// and a stale anchor hands over real text nobody dragged across.
    /// </summary>
    [Test]
    public async Task FreezingAndThawingDropsTheSelectionRatherThanReAnchoringIt()
    {
        var (app, copied) = WithClipboard();
        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 20, 1);
        await Assert.That(app.PaneSelection(SharpMUTermApp.MainWindowId)).IsNotEmpty();

        app.DispatchCommand("term:freeze");
        app.DispatchCommand("term:unfreeze");
        app.SimulateKey(CtrlC);

        await Assert.That(copied).IsEmpty();
        await Assert.That(app.StatusMarkup).Contains("nothing selected");
    }

    /// <summary>The ⌃P entry and the chord are one action, and the entry is how the chord is found at all.</summary>
    [Test]
    public async Task TheCommandSurfaceCopiesTheSameText()
    {
        var (app, copied) = WithClipboard();
        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 25, 1);

        await Assert.That(app.DispatchCommand("term:copy")).IsTrue();

        await Assert.That(copied).HasSingleItem();
    }

    [Test]
    public async Task DraggingAcrossAPaneSelectsTheTextUnderThePointer()
    {
        var app = Rendered();

        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 12, 0);

        await Assert.That(app.PaneSelection(SharpMUTermApp.MainWindowId)).IsNotEmpty();
    }

    /// <summary>
    /// What is copied is what the pane <em>shows</em>, not the markup behind it. <c>Source</c> mode would
    /// hand back <c>[bold #ff0000]…[/]</c>, which is not what anyone dragged over.
    /// </summary>
    [Test]
    public async Task TheSelectedTextCarriesNoMarkup()
    {
        var app = Rendered();

        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 40, 2);
        var selected = app.PaneSelection(SharpMUTermApp.MainWindowId);

        await Assert.That(selected).DoesNotContain("[/]");
        await Assert.That(selected).DoesNotContain("[bold");
    }

    /// <summary>
    /// A pane with nothing selected reports nothing — the state every pane is in until a drag happens, and
    /// the one the copy shortcut must not fire in.
    /// </summary>
    [Test]
    public async Task AFreshPaneHasNoSelection()
    {
        var app = Rendered();

        await Assert.That(app.PaneSelection(SharpMUTermApp.MainWindowId)).IsEmpty();
    }

    /// <summary>
    /// The pin that stops this being "improved" into something that spends a cell. A selection recolours
    /// cells that are already painted; if it ever gained a gutter or a marker column the pane rectangle
    /// would change, and per-pane NAWS is derived from that rectangle — so dragging in a pane would
    /// announce a new terminal size to every connected server and reflow the game's own output.
    /// </summary>
    [Test]
    public async Task SelectingTextMovesNoPaneRectangle()
    {
        var app = Rendered();
        var before = app.PaneOutputRects();

        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 30, 3);
        app.RenderWholeFrame();

        await Assert.That(app.PaneOutputRects()).IsEquivalentTo(before);
    }

    /// <summary>
    /// A buffer that shifts under a live selection leaves it pointing at rows that have moved — the client
    /// inserts and removes chrome rows mid-buffer (the freeze bar, the away bar, the <c>NEW</c> divider)
    /// and repaints whole buffers when the timestamp column is toggled. The selection is dropped on those
    /// paths rather than left to describe a stale grid.
    /// </summary>
    [Test]
    public async Task RepaintingAPaneDropsASelectionThatWouldNowPointAtTheWrongRows()
    {
        var app = Rendered();
        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 20, 1);
        await Assert.That(app.PaneSelection(SharpMUTermApp.MainWindowId)).IsNotEmpty();

        app.DispatchCommand("term:timestamps-on"); // the whole-buffer re-feed

        await Assert.That(app.PaneSelection(SharpMUTermApp.MainWindowId)).IsEmpty();
    }
}
