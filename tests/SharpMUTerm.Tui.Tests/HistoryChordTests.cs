using SharpConsoleUI.Drivers;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// ⌥↑/⌥↓ mean history and nothing else. The bare arrows still recall at the edges — nothing is taken
/// away — but they answer to the caret first, which stops being a usable rule the moment the command
/// line grows to a second row. That was the reported complaint.
/// <para>
/// ⌃↑/⌃↓, the other chord the request offered, was never available: the terminal writes `ESC [ 1;5 A`
/// for it and this client already spends that on pane selection and on the ladder onto the second
/// command line. `ESC [ 1;3 A` — Alt — is free, and was measured at a raw reader before it was spent.
/// </para>
/// </summary>
/// <remarks>Serialised: constructing the app touches the process-global console streams.</remarks>
[NotInParallel]
public class HistoryChordTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App()
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(120, 34));
        app.RenderSnapshot();
        return app;
    }

    private static ConsoleKeyInfo Chord(ConsoleKey key, bool alt = false) =>
        new('\0', key, shift: false, alt: alt, control: false);

    /// <summary>One printable character, as the command line's own tests spell it.</summary>
    private static ConsoleKeyInfo Key(char c) => new(c, ConsoleKey.NoName, false, false, false);

    private static void Type(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(Key(c));
        }
    }

    private static void Send(SharpMUTermApp app, string text)
    {
        Type(app, text);
        app.SimulateKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

    /// <summary>
    /// Sends two lines so the history has something in it, and leaves the bar empty. The leading empty
    /// send is what clears it: the demo scene seeds a draft into the command line, and typing on top of
    /// that would send — and recall — a line nobody typed.
    /// </summary>
    private static void Seed(SharpMUTermApp app)
    {
        Send(app, string.Empty);
        Send(app, "look");
        Send(app, "say hello");
    }

    [Test]
    public async Task AltUpRecallsTheNewestLineAndAltDownWalksBack()
    {
        var app = App();
        Seed(app);

        app.SimulateKey(Chord(ConsoleKey.UpArrow, alt: true));
        await Assert.That(app.ArmedInputText).IsEqualTo("say hello");

        app.SimulateKey(Chord(ConsoleKey.UpArrow, alt: true));
        await Assert.That(app.ArmedInputText).IsEqualTo("look");

        app.SimulateKey(Chord(ConsoleKey.DownArrow, alt: true));
        await Assert.That(app.ArmedInputText).IsEqualTo("say hello");
    }

    /// <summary>
    /// The point of the chord. With a draft tall enough to have a row above the caret, the bare ↑ is the
    /// caret's and ⌥↑ is history's — on the same keystroke, from the same position.
    /// </summary>
    [Test]
    public async Task OnAGrownBarTheBareArrowMovesTheCaretAndTheAltArrowRecalls()
    {
        var app = App();
        Seed(app);
        var draft = new string('x', 400); // wraps to several rows at 120 columns
        Type(app, draft);

        app.SimulateKey(Chord(ConsoleKey.UpArrow));
        await Assert.That(app.ArmedInputText).IsEqualTo(draft);

        app.SimulateKey(Chord(ConsoleKey.UpArrow, alt: true));
        await Assert.That(app.ArmedInputText).IsEqualTo("say hello");
    }

    /// <summary>
    /// ⌥↓ with nothing recalled is not ours: it must not clear the bar. Exactly the bare-arrow rule,
    /// which declines rather than blanking a draft.
    /// </summary>
    [Test]
    public async Task AltDownWithNothingRecalledLeavesTheDraftAlone()
    {
        var app = App();
        Seed(app);
        Type(app, "half a thought");

        app.SimulateKey(Chord(ConsoleKey.DownArrow, alt: true));

        await Assert.That(app.ArmedInputText).IsEqualTo("half a thought");
    }

    /// <summary>
    /// The bare arrows are unchanged: on a single-row bar they still recall, because the caret has
    /// nowhere further to go. Nothing about this feature takes that away.
    /// </summary>
    [Test]
    public async Task TheBareArrowsStillRecallAtTheEdges()
    {
        var app = App();
        Seed(app);

        app.SimulateKey(Chord(ConsoleKey.UpArrow));

        await Assert.That(app.ArmedInputText).IsEqualTo("say hello");
    }
}
