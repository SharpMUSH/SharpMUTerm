using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The boundary marking a window the reader was not watching — the generalisation of
/// <see cref="AwayDividerTests"/>' terminal absence to the one that happens many times an hour: a tab
/// behind another tab, or a pane you have scrolled back in.
/// <para>
/// It answers to <c>Workspace.IsCaughtUp</c>, which is the rule the unread badge already uses, and that
/// is the point rather than a coincidence: a badge showing a count must always have a bar under it
/// saying where the count begins. The badge said 3 and nothing said which 3 — that was the report.
/// </para>
/// </summary>
/// <remarks>
/// Serialised for the reason every file that renders a frame is: rendering redirects the process-global
/// <c>Console.Out</c>, and the harness redirects <c>Console.In</c>.
/// </remarks>
[NotInParallel]
public class WindowActivityBoundaryTests
{
    private const int Width = 120;
    private const int Height = 32;

    /// <summary>The window the demo scene's own output lands in.</summary>
    private const string Main = "main";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    /// <summary>
    /// The demo configuration with the scrollback spill off — these tests print more than a session's
    /// in-memory ring holds, and a spilling session writes into the developer's own cache directory,
    /// which <c>UserDirectoryGuard</c> fails the run for.
    /// </summary>
    private static (SharpMUTermApp App, WorldSession Session) Bound()
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height));
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);
        app.RenderSnapshot();
        return (app, session);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, bool ctrl = false) => new('\0', key, false, false, ctrl);

    /// <summary>
    /// The reported case. Lines land in a window sitting behind another tab; going to it puts the reader
    /// on a boundary with exactly those lines under it.
    /// </summary>
    [Test]
    public async Task AWindowBehindAnotherTabAccruesABoundaryAndShowsItOnReturn()
    {
        var (app, session) = Bound();
        app.SimulateWindowChange(DemoScene.ChatWindowId);

        session.PrintSystem("*** while you were reading Chat");
        session.PrintSystem("*** and again");
        await Assert.That(app.AwayBarIndex(Main)).IsNull();

        app.SimulateWindowChange(Main);

        var index = app.AwayBarIndex(Main);
        await Assert.That(index).IsNotNull();

        var rows = app.PaneLines(Main);
        await Assert.That(rows[index!.Value]).Contains(AwayBarRenderer.MissedLabel);
        await Assert.That(rows[index.Value]).Contains("2 lines since you were here");

        // The bar marks the boundary, so what was missed is under it and what was already read is above.
        await Assert.That(rows[index.Value + 1]).Contains("while you were reading Chat");
    }

    /// <summary>
    /// The window in front of the reader, at its live tail, accrues nothing: those lines went past their
    /// eyes. The same rule the unread badge answers to, which is why the two cannot disagree.
    /// </summary>
    [Test]
    public async Task TheWindowInFrontOfTheReaderGetsNoBar()
    {
        var (app, session) = Bound();

        session.PrintSystem("*** the goblin snarls at you");
        app.RenderSnapshot();

        await Assert.That(app.AwayBarIndex(Main)).IsNull();
        await Assert.That(app.PaneLines(Main).Any(row => row.Contains("since you were here"))).IsFalse();
    }

    /// <summary>
    /// A window the reader is looking at but has scrolled back off is exactly as blind as one they are
    /// not looking at — <c>OnLine</c>'s reasoning for the badge, one field over — so it accrues, and the
    /// bar is there when they come back down to the tail.
    /// </summary>
    [Test]
    public async Task AVisibleWindowScrolledBackAccruesAndTheBarIsThereOnTheWayBackDown()
    {
        var (app, session) = Bound();
        for (var i = 1; i <= 80; i++)
        {
            session.PrintSystem($"*** line {i}");
        }

        app.RenderSnapshot();
        app.SimulateScrollKey(Key(ConsoleKey.PageUp));
        app.RenderSnapshot();

        session.PrintSystem("*** arrived while you were reading back");
        await Assert.That(app.AwayBarIndex(Main)).IsNull();

        app.SimulateScrollKey(Key(ConsoleKey.End, ctrl: true));

        var index = app.AwayBarIndex(Main);
        await Assert.That(index).IsNotNull();
        await Assert.That(app.PaneLines(Main)[index!.Value]).Contains(AwayBarRenderer.MissedLabel);
    }

    /// <summary>
    /// Two boundaries can exist for one window, and the older wins. A window that had already missed
    /// lines before the reader left the terminal must not have its bar moved <em>down</em> to where they
    /// left: that would hide the very lines it was made for.
    /// </summary>
    [Test]
    public async Task TheOlderOfTheTwoBoundariesWins()
    {
        var (app, session) = Bound();
        app.SimulateWindowChange(DemoScene.ChatWindowId);

        session.PrintSystem("*** missed while the tab was behind Chat");

        // Two keystrokes with a line between them, so the terminal absence's own boundary — which is
        // reconstructed from the input before the last one — lands *after* the window's.
        app.SimulateKey(Key(ConsoleKey.End));
        session.PrintSystem("*** and this one too");
        app.SimulateKey(Key(ConsoleKey.End));

        app.SimulateReturnFromAway(TimeSpan.FromMinutes(12));

        var index = app.AwayBarIndex(Main);
        await Assert.That(index).IsNotNull();
        await Assert.That(app.PaneLines(Main)[index!.Value + 1]).Contains("missed while the tab was behind Chat");
    }
}
