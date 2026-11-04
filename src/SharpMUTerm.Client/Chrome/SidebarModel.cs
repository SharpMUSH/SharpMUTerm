using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Client.Chrome;

/// <summary>
/// The connection sidebar as data: worlds, their characters, and under the character in front its windows — each with
/// its state, unread count, draft pen, the chord that reaches it and whether it is the one selected. No presentation
/// in it. The terminal draws it as rows of cells (<see cref="Rows()"/>, through <c>RailRenderer</c>); the window draws a
/// native sidebar over those cells from the same instance, so the two cannot disagree about a dot, a count or a key.
/// <para>
/// <b>The tree is Core's</b> (<see cref="RailWorld"/> → <see cref="RailCharacter"/> → <see cref="RailWindow"/>), which the
/// shell already projects from configuration and workspace; <see cref="RailModel"/> in Core is the flat-row projection
/// of that same tree, and this type is the one both views are handed. What it adds is what only a view-model needs:
/// which window is in front, whether the rail is collapsed, the click target of each node, and value equality, so a
/// host is told only when something it draws has changed.
/// </para>
/// </summary>
/// <param name="Worlds">The worlds in configuration order, each with its characters.</param>
/// <param name="SelectedWindowId">The window in front of the focused pane, or null.</param>
/// <param name="Collapsed">Whether the rail is the narrow strip (⌃B b) rather than the full sidebar.</param>
public sealed record SidebarModel(IReadOnlyList<RailWorld> Worlds, string? SelectedWindowId, bool Collapsed)
{
    /// <summary>No worlds: the sidebar of a client with nothing configured.</summary>
    public static SidebarModel Empty { get; } = new([], null, false);

    /// <summary>
    /// The click target of the sidebar's own "add a game" button: the welcome surface, where a game is connected or
    /// added. A command id the ⌃P surface already dispatches, so the button is one more door onto it.
    /// </summary>
    public const string AddGameTarget = "term:welcome";

    /// <summary>The flat rows the cell rail draws (<see cref="RailModel.Build"/>).</summary>
    public IReadOnlyList<RailRow> Rows() => RailModel.Build(Worlds);

    /// <summary>Where a click on a world goes: the character already in front there, else a live one, else the first.</summary>
    public static string TargetOf(RailWorld world) => RailModel.TargetFor(world);

    /// <summary>Where a click on a character goes: that character, opened or brought forward.</summary>
    public static string TargetOf(RailCharacter character) => CommandIds.Character(character.SessionKey);

    /// <summary>Where a click on a window goes: that window, brought forward.</summary>
    public static string TargetOf(RailWindow window) => CommandIds.Window(window.Id);

    /// <summary>Whether <paramref name="window"/> is the one in front of the focused pane.</summary>
    public bool IsSelected(RailWindow window) => string.Equals(window.Id, SelectedWindowId, StringComparison.Ordinal);

    /// <summary>
    /// Every click target this model can emit, so a host's answer can be checked against what the client offered: a
    /// target that is not in this set did not come from the sidebar.
    /// </summary>
    public IEnumerable<string> Targets()
    {
        yield return AddGameTarget;
        foreach (var world in Worlds)
        {
            yield return TargetOf(world);
            foreach (var character in world.Characters)
            {
                yield return TargetOf(character);
                foreach (var window in character.Windows)
                {
                    yield return TargetOf(window);
                }
            }
        }
    }

    /// <summary>Value equality over the whole tree, which a record's list members do not give.</summary>
    public bool Equals(SidebarModel? other) =>
        other is not null
        && Collapsed == other.Collapsed
        && string.Equals(SelectedWindowId, other.SelectedWindowId, StringComparison.Ordinal)
        && Worlds.Count == other.Worlds.Count
        && Worlds.Zip(other.Worlds).All(p => SameWorld(p.First, p.Second));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Collapsed);
        hash.Add(SelectedWindowId);
        foreach (var world in Worlds)
        {
            hash.Add(world.Name);
            hash.Add(world.Characters.Count);
        }

        return hash.ToHashCode();
    }

    private static bool SameWorld(RailWorld a, RailWorld b) =>
        a.Name == b.Name && a.Host == b.Host && a.Port == b.Port && a.Accent.Equals(b.Accent)
        && a.Characters.Count == b.Characters.Count
        && a.Characters.Zip(b.Characters).All(p => SameCharacter(p.First, p.Second));

    private static bool SameCharacter(RailCharacter a, RailCharacter b) =>
        a.Name == b.Name && a.SessionKey == b.SessionKey && a.Connected == b.Connected && a.Active == b.Active
        && a.Unread == b.Unread && a.Chord == b.Chord && a.InGroup == b.InGroup
        && a.Windows.SequenceEqual(b.Windows); // RailWindow has no list member, so its record equality is by value
}
