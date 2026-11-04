using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Tui;

/// <summary>
/// A pane narrower than the server's wrap column: every hard-wrapped row of a room description then wraps a second time
/// and leaves a stray word on a row of its own. That is what a layout's side panels, a split or a narrow window do to an
/// 80-column server. The client says so once per world per run, with an action that turns F5's <c>join wraps</c> on.
/// <para>
/// <b>Suggested, never switched on for the player.</b> A false join is worse than a missed one (see
/// <see cref="Core.Text.ServerWrapJoiner"/>): it runs two lines the server meant apart into one, and nothing on screen
/// says it happened. Whether a world's output is the prose the joiner is safe on is the player's to judge, so the client
/// names the cause and the one-click fix and leaves the setting where it is.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The worlds this run has already been told about, by name.</summary>
    private readonly HashSet<string> _joinWrapsSuggested = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Says, once per world, that <paramref name="session"/>'s pane is narrower than its server wraps — when the wrap
    /// column is known, the world does not join wraps, and the pane is at least a few columns short of it. Called with
    /// each pane width the client reports to the server.
    /// </summary>
    private void SuggestJoinWraps(WorldSession session, int paneWidth)
    {
        if (session.World.JoinWrappedLines
            || session.ServerWrapColumn is not { } column
            || paneWidth <= 0
            || paneWidth >= column - 2
            || _joinWrapsSuggested.Contains(session.World.Name))
        {
            return;
        }

        _joinWrapsSuggested.Add(session.World.Name);
        Notice(
            $"{Snippet(session.World.Name)} wraps at {column} and this pane is {paneWidth} wide · join wraps re-flows it (F5)",
            MessageSeverity.Info,
            action: new NoticeAction(BuiltInCommands.JoinWrapsPrefix + session.World.Name, "Join wraps"));
    }

    /// <summary><c>world:join-wraps:&lt;world&gt;</c>: turns F5's <c>join wraps</c> on for that world, from the next line.</summary>
    private bool JoinWrapsFor(string worldName)
    {
        var world = _config.Worlds.FirstOrDefault(w => string.Equals(w.Name, worldName, StringComparison.OrdinalIgnoreCase));
        if (world is null)
        {
            RefuseCommand($"there is no world {Snippet(worldName)}");
            return true;
        }

        if (!world.JoinWrappedLines)
        {
            world.JoinWrappedLines = true;
            SaveConfiguration();
        }

        Notice($"{Snippet(world.Name)} joins its wrapped lines from the next line · F5 ▸ join wraps", MessageSeverity.Info);
        return true;
    }
}
