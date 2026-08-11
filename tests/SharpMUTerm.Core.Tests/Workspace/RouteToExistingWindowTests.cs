using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Tests.Workspaces;

/// <summary>
/// The reported defect: <b>a trigger's routing could only ever go to a spawn window.</b>
/// <c>Workspace.RouteSpawn</c> is the one destination resolver a matched rule has, and it computes
/// <c>SpawnWindowId(sessionKey, target)</c> and — when nothing answers to that id — registers a brand
/// new <see cref="WindowKind.Spawn"/> window. There is no branch in it that can reach a window that
/// already exists under any other name, so "route to the window I already have open" was not a thing a
/// rule could ask for however it was spelt.
/// <para>
/// <see cref="Workspace.RouteLine"/> is the resolver now: an existing window the target names wins, and
/// creating a spawn is what happens when nothing does. What that buys is a rule feeding a window
/// somebody opened deliberately — a character's own main window, another character's, or any named
/// auxiliary window — rather than a fourth pane appearing beside them.
/// </para>
/// <para>
/// <b>What it deliberately cannot reach is another session's spawn window</b>, and that is the whole
/// reason resolution is scoped rather than a bare title lookup over the registry. Two characters running
/// one capture rule get a pane each; a title that crossed between them would collapse the two back into
/// one and file the second character's channel under the first, which is exactly the defect
/// <c>SpawnWindowId</c> was given an owner to fix.
/// </para>
/// </summary>
public class RouteToExistingWindowTests
{
    private const string Ann = "Convergence.Ann";
    private const string Bob = "Convergence.Bob";

    /// <summary>A workspace whose main window belongs to Ann and is titled the way the shell titles it.</summary>
    private static Workspace AnnsWorkspace() => new("main", "Ann", Ann);

    // ---- The report -------------------------------------------------------------------------

    /// <summary>
    /// The headline. A rule routing to the title of a window that already exists lands in <em>that</em>
    /// window; before the fix it opened a second one beside it, of a different kind, with the same name on
    /// its tab.
    /// </summary>
    [Test]
    public async Task ARuleRoutesToAnExistingWindowRatherThanOpeningASpawnBesideIt()
    {
        var workspace = AnnsWorkspace();
        workspace.OpenWindow("notes", "Notes", WindowKind.Auxiliary, Ann);

        var destination = workspace.RouteLine("Notes", Ann);

        await Assert.That(destination.Id).IsEqualTo("notes");
        await Assert.That(workspace.Windows.Count(w => w.Title == "Notes")).IsEqualTo(1);
    }

    /// <summary>
    /// Including the character's own main window, which is the destination the F2 route list has always
    /// named and the one a rule could least express: <c>main</c> there means <em>do not route</em>, which
    /// only reaches the main window for a line the rule does not also gag.
    /// </summary>
    [Test]
    public async Task ARuleCanRouteToItsOwnCharactersMainWindow()
    {
        var workspace = AnnsWorkspace();

        var destination = workspace.RouteLine("Ann", Ann);

        await Assert.That(destination.Id).IsEqualTo("main");
        await Assert.That(destination.Kind).IsEqualTo(WindowKind.Main);
    }

    /// <summary>
    /// And another character's main window — one alt's channel collected into the pane you actually read.
    /// A main window is the one window another session owns that this may reach, because it is a window
    /// the user opened by connecting rather than one a capture rule conjured.
    /// </summary>
    [Test]
    public async Task ARuleCanRouteToAnotherCharactersMainWindow()
    {
        var workspace = AnnsWorkspace();
        workspace.OpenWindow("main:bob", "Bob", WindowKind.Main, Bob);

        var destination = workspace.RouteLine("Bob", Ann);

        await Assert.That(destination.Id).IsEqualTo("main:bob");
    }

    /// <summary>An unowned window — the web view is the one in this client — is in everybody's reach.</summary>
    [Test]
    public async Task ARuleCanRouteToAnUnownedWindow()
    {
        var workspace = AnnsWorkspace();
        workspace.OpenWindow("web", "Scratch", WindowKind.Auxiliary);

        await Assert.That(workspace.RouteLine("Scratch", Ann).Id).IsEqualTo("web");
    }

    // ---- What must not move ------------------------------------------------------------------

    /// <summary>
    /// The per-session guarantee, which this resolution is scoped to preserve: Bob's rule may not land in
    /// Ann's capture pane just because they chose the same channel name. Bob gets his own, as before.
    /// </summary>
    [Test]
    public async Task ARuleCannotRouteIntoAnotherSessionsSpawnWindow()
    {
        var workspace = AnnsWorkspace();
        var anns = workspace.RouteLine("Public", Ann);

        var bobs = workspace.RouteLine("Public", Bob);

        await Assert.That(bobs.Id).IsNotEqualTo(anns.Id);
        await Assert.That(bobs.Id).IsEqualTo(Workspace.SpawnWindowId(Bob, "Public"));
        await Assert.That(bobs.SessionKey).IsEqualTo(Bob);
    }

    /// <summary>
    /// Nor into another session's auxiliary window. Only a <em>main</em> window crosses the owner
    /// boundary: everything else another character owns was created for them, and the two cases a route
    /// must never conflate are "the window you meant" and "somebody else's window with the same label".
    /// </summary>
    [Test]
    public async Task ARuleCannotRouteIntoAnotherSessionsAuxiliaryWindow()
    {
        var workspace = AnnsWorkspace();
        workspace.OpenWindow("bobs-notes", "Notes", WindowKind.Auxiliary, Bob);

        var destination = workspace.RouteLine("Notes", Ann);

        await Assert.That(destination.Id).IsEqualTo(Workspace.SpawnWindowId(Ann, "Notes"));
    }

    /// <summary>
    /// Nothing found is still a spawn window, created and placed exactly as it always was. This is the
    /// path every existing capture rule takes and it must be untouched.
    /// </summary>
    [Test]
    public async Task ATargetNothingAnswersToStillOpensASpawnWindow()
    {
        var workspace = AnnsWorkspace();

        var destination = workspace.RouteLine("Chat", Ann);

        await Assert.That(destination.Id).IsEqualTo(Workspace.SpawnWindowId(Ann, "Chat"));
        await Assert.That(destination.Kind).IsEqualTo(WindowKind.Spawn);
        await Assert.That(destination.Title).IsEqualTo("Chat");
        await Assert.That(workspace.Layout.FindWindow(destination.Id)).IsNotNull();
    }

    /// <summary>
    /// A rule feeding its own capture pane goes on feeding the same one — the second line of a channel
    /// must not find the window by a different route than the first did and end up somewhere else.
    /// </summary>
    [Test]
    public async Task TheSecondLineOfACaptureLandsInTheSamePaneAsTheFirst()
    {
        var workspace = AnnsWorkspace();

        var first = workspace.RouteLine("Chat", Ann);
        var second = workspace.RouteLine("Chat", Ann);

        await Assert.That(second.Id).IsEqualTo(first.Id);
    }

    /// <summary>
    /// A window no pane holds is not a destination. Routing there would append to a buffer nothing can
    /// draw, which is indistinguishable from the rule not firing — so a closed window is passed over and
    /// the line goes to a spawn pane that can actually be seen.
    /// </summary>
    [Test]
    public async Task AClosedWindowIsNotADestination()
    {
        var workspace = AnnsWorkspace();
        workspace.OpenWindow("notes", "Notes", WindowKind.Auxiliary, Ann);
        workspace.CloseWindow("notes");

        var destination = workspace.RouteLine("Notes", Ann);

        await Assert.That(destination.Id).IsEqualTo(Workspace.SpawnWindowId(Ann, "Notes"));
    }

    /// <summary>
    /// The same rule through the renamed-spawn fallback, which is the one arm that does not go by title.
    /// A spawn window whose pane the user closed is still in the registry — the registry outlives the
    /// layout, and a restored workspace can register windows a saved layout no longer places — so the
    /// fallback would hand back a window nothing draws. It has to place the pane again instead, under
    /// the same id, so the channel comes back with its history rather than going somewhere invisible.
    /// </summary>
    [Test]
    public async Task ARenamedSpawnWindowWhosePaneWasClosedIsPlacedAgainRatherThanFedInvisibly()
    {
        var workspace = AnnsWorkspace();
        var spawned = workspace.RouteLine("Chat", Ann);
        spawned.Title = "Tells"; // the user renames it, so no title answers to "Chat" any more
        workspace.Layout.RemoveWindow(spawned.Id); // and closes its pane, leaving it registered

        await Assert.That(workspace.FindRouteTarget("Chat", Ann)).IsNull();

        var destination = workspace.RouteLine("Chat", Ann);

        await Assert.That(destination.Id).IsEqualTo(spawned.Id);
        await Assert.That(workspace.Layout.FindWindow(destination.Id)).IsNotNull();
    }

    /// <summary>
    /// Routing badges the destination unread when it is not the window being read, whichever kind it
    /// turned out to be. The badge is the only thing that says a background pane gained a line, and a
    /// resolution that reached a new kind of window without it would make the feature silent.
    /// </summary>
    [Test]
    public async Task RoutingToAnExistingWindowStillBadgesItUnread()
    {
        var workspace = AnnsWorkspace();
        workspace.OpenWindow("notes", "Notes", WindowKind.Auxiliary, Ann);
        workspace.ActivateWindow("main"); // Notes shares the pane as a tab, and is now the hidden one

        var destination = workspace.RouteLine("Notes", Ann);

        await Assert.That(destination.Unread).IsGreaterThan(0);
    }
}
