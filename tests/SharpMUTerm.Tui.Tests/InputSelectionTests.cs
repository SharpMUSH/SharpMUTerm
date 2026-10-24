using SharpConsoleUI.Drivers;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// <b>Copying from the command line.</b> The request: "I need a way to copy from my input window." The bar
/// takes part in the window's one selection — the same <c>SelectionManager</c> a pane's drag registers
/// with — so ⌃C copies whichever was made last, and starting one clears the other. Driven through the real
/// key chain and the bar's real mouse handler.
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class InputSelectionTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static ConsoleKeyInfo CtrlC => Ctrl(ConsoleKey.C);

    private static ConsoleKeyInfo Ctrl(ConsoleKey key) => new('\0', key, shift: false, alt: false, control: true);

    private static ConsoleKeyInfo Shifted(ConsoleKey key) => new('\0', key, shift: true, alt: false, control: false);

    private static (SharpMUTermApp App, List<string> Copied) App(string draft, bool clipboard = true)
    {
        Console.SetIn(TextReader.Null);
        var copied = new List<string>();
        var app = new SharpMUTermApp(
            DemoScene.Build(), Headless, new HeadlessConsoleDriver(120, 32),
            clipboard: clipboard ? copied.Add : null);
        app.RenderSnapshot();

        // The demo seeds a draft; send it away so the bar holds exactly what the test types.
        app.SimulateKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        foreach (var c in draft)
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.RenderNextFrame();
        return (app, copied);
    }

    [Test]
    public async Task ShiftHomeThenCtrlCCopiesTheCommandLine()
    {
        var (app, copied) = App("page Ann=see you at the docks");

        app.SimulateKey(Shifted(ConsoleKey.Home));
        app.SimulateKey(CtrlC);

        await Assert.That(copied).IsEquivalentTo(new[] { "page Ann=see you at the docks" });
        await Assert.That(app.ArmedInputText)
            .IsEqualTo("page Ann=see you at the docks")
            .Because("copying is not cutting");
    }

    [Test]
    public async Task DraggingAcrossTheCommandLineSelectsWhatItCrosses()
    {
        var (app, copied) = App("say hello there");
        var prompt = SharpConsoleUI.Parsing.MarkupParser.StripLength(app.PrimaryPromptMarkup);

        app.SimulateInputDrag(prompt + 4, 0, prompt + 9, 0);
        app.SimulateKey(CtrlC);

        await Assert.That(app.InputSelection).IsEqualTo("hello");
        await Assert.That(copied).IsEquivalentTo(new[] { "hello" });
    }

    /// <summary>One selection per window: a pane's drag takes it from the bar, and the bar's from a pane.</summary>
    [Test]
    public async Task ThePaneAndTheCommandLineShareOneSelection()
    {
        var (app, copied) = App("say hello");

        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 25, 1);
        var pane = app.PaneSelection(SharpMUTermApp.MainWindowId);
        await Assert.That(pane).IsNotEmpty();

        app.SimulateKey(Shifted(ConsoleKey.Home));
        await Assert.That(app.PaneSelection(SharpMUTermApp.MainWindowId))
            .IsEmpty()
            .Because("starting a selection in the command line clears the pane's");
        app.SimulateKey(CtrlC);

        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 25, 1);
        await Assert.That(app.InputSelection).IsEmpty();
        app.SimulateKey(CtrlC);

        await Assert.That(copied).IsEquivalentTo(new[] { "say hello", pane });
    }

    /// <summary>
    /// ⌃A selects the command line through the window's one selection, so ⌃C finds it and a pane's
    /// selection gives way to it.
    /// </summary>
    [Test]
    public async Task CtrlAThenCtrlCCopiesTheWholeCommandLine()
    {
        var (app, copied) = App("page Ann=see you at the docks");
        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 25, 1);

        app.SimulateKey(Ctrl(ConsoleKey.A));
        await Assert.That(app.PaneSelection(SharpMUTermApp.MainWindowId)).IsEmpty();
        app.SimulateKey(CtrlC);

        await Assert.That(copied).IsEquivalentTo(new[] { "page Ann=see you at the docks" });
    }

    [Test]
    public async Task CtrlXCutsTheCommandLinesSelectionToTheClipboard()
    {
        var (app, copied) = App("say hello there");
        var prompt = SharpConsoleUI.Parsing.MarkupParser.StripLength(app.PrimaryPromptMarkup);

        app.SimulateInputDrag(prompt + 3, 0, prompt + 9, 0);
        app.SimulateKey(Ctrl(ConsoleKey.X));

        await Assert.That(copied).IsEquivalentTo(new[] { " hello" });
        await Assert.That(app.ArmedInputText).IsEqualTo("say there");
    }

    /// <summary>With no clipboard to put it on, a cut would only delete, so it refuses and keeps the text.</summary>
    [Test]
    public async Task CtrlXWithNoClipboardDeletesNothing()
    {
        var (app, _) = App("say hello", clipboard: false);

        app.SimulateKey(Ctrl(ConsoleKey.A));
        app.SimulateKey(Ctrl(ConsoleKey.X));

        await Assert.That(app.ArmedInputText).IsEqualTo("say hello");
    }
}
