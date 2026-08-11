using SharpConsoleUI.Drivers;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Freeze answers to ⌥F and not to ⌃F, which search takes in the PR after this one. Both halves are
/// the claim: a chord that moved in the handler and not in the bar would leave the client telling a
/// frozen reader to press a key that no longer thaws it.
/// </summary>
/// <remarks>Serialised: constructing the app touches the process-global console streams.</remarks>
[NotInParallel]
public class FreezeChordTests
{
    private const string Main = "main";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App()
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(120, 34));
        app.RenderSnapshot();
        return app;
    }

    private static ConsoleKeyInfo Chord(ConsoleKey key, bool alt = false, bool control = false) =>
        new('\0', key, shift: false, alt: alt, control: control);

    /// <summary>
    /// Read off the frame rather than off <c>FrozenScrollbackOf</c>: the pinned-scrollback viewport is
    /// created on first freeze and <em>kept</em> for the life of the window, so it is a poor oracle for
    /// "is this pane frozen right now" and answers non-null for ever after the first ⌥F. The bar in the
    /// paint is the thing the reader is actually looking at.
    /// </summary>
    [Test]
    public async Task AltFFreezesTheFocusedPaneAndPressingItAgainResumes()
    {
        var app = App();
        await Assert.That(app.RenderSnapshot()).DoesNotContain("FROZEN");

        app.SimulateKey(Chord(ConsoleKey.F, alt: true));
        await Assert.That(app.RenderSnapshot()).Contains("FROZEN ⌥F");

        app.SimulateKey(Chord(ConsoleKey.F, alt: true));
        await Assert.That(app.RenderSnapshot()).DoesNotContain("FROZEN");
    }

    [Test]
    public async Task CtrlFNoLongerFreezesAnything()
    {
        var app = App();

        app.SimulateKey(Chord(ConsoleKey.F, control: true));

        await Assert.That(app.RenderSnapshot()).DoesNotContain("FROZEN");
        await Assert.That(app.FrozenScrollbackOf(Main)).IsNull();
    }

    /// <summary>
    /// Both halves of the move: ⌥F is freeze, and ⌃F is claimed by something that is not freeze. The
    /// second half matters because the whole point of the move was to hand ⌃F to find — a claim list
    /// where it had simply gone missing would mean the chord had been spent on nothing.
    /// </summary>
    [Test]
    public async Task TheClaimListNamesAltFForFreezeAndCtrlFForSomethingElse()
    {
        var claims = MacroKeys.AppShortcuts;

        var alt = claims.Single(c => c.Modifiers == ConsoleModifiers.Alt && c.Key == ConsoleKey.F);
        await Assert.That(alt.Does).Contains("freeze");

        var ctrl = claims.Single(c => c.Modifiers == ConsoleModifiers.Control && c.Key == ConsoleKey.F);
        await Assert.That(ctrl.Does).DoesNotContain("freeze");
    }
}
