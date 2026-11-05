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
/// <param name="Cursor">
/// The row the keyboard is on (<see cref="KeyOf(RailRow)"/>) while the sidebar holds the keys (⌥C), or null. Both views
/// draw it — the cell rail as a band across the row, the native sidebar as a focus ring — so a reader sees where ⏎ goes
/// in either host.
/// </param>
public sealed record SidebarModel(IReadOnlyList<RailWorld> Worlds, string? SelectedWindowId, bool Collapsed, string? Cursor = null)
{
    /// <summary>No worlds: the sidebar of a client with nothing configured.</summary>
    public static SidebarModel Empty { get; } = new([], null, false);

    /// <summary>
    /// The click target of the sidebar's own "add a game" button: the welcome surface, where a game is connected or
    /// added. A command id the ⌃P surface already dispatches, so the button is one more door onto it.
    /// </summary>
    public const string AddGameTarget = "term:welcome";

    /// <summary>
    /// The sidebar's own target for folding it to its slim strip and back: what ⌃B b does. A chrome target, never a
    /// server's — only the chrome's own handler reads it — and offered only by a host that draws the sidebar itself,
    /// because the cell rail is folded with the keys.
    /// </summary>
    public const string CollapseTarget = "sharpmuterm-rail:toggle";

    /// <summary>The flat rows the cell rail draws (<see cref="RailModel.Build"/>).</summary>
    public IReadOnlyList<RailRow> Rows() => RailModel.Build(Worlds);

    /// <summary>
    /// A row's identity, which outlives a rebuild of the model: its kind and its click target. The kind is part of it
    /// because a world's target is one of its characters', and the two rows are different places to stand.
    /// </summary>
    public static string KeyOf(RailRowKind kind, string? target) => $"{kind}|{target}";

    /// <inheritdoc cref="KeyOf(RailRowKind, string?)"/>
    public static string KeyOf(RailRow row) => KeyOf(row.Kind, row.Target);

    /// <summary>
    /// The rows the keyboard walks, in the order both views draw them: every row with a target, and while folded only
    /// the worlds and characters, because the strip draws nothing else.
    /// </summary>
    public IReadOnlyList<RailRow> NavigableRows() =>
        [.. Rows().Where(r => r.Target is not null && (!Collapsed || r.Kind is RailRowKind.World or RailRowKind.Character))];

    /// <summary>Where a click on a world goes: the character already in front there, else a live one, else the first.</summary>
    public static string TargetOf(RailWorld world) => RailModel.TargetFor(world);

    /// <summary>Where a click on a character goes: that character, opened or brought forward.</summary>
    public static string TargetOf(RailCharacter character) => CommandIds.Character(character.SessionKey);

    /// <summary>Where a click on a window goes: that window, brought forward.</summary>
    public static string TargetOf(RailWindow window) => CommandIds.Window(window.Id);

    /// <summary>Whether <paramref name="window"/> is the one in front of the focused pane.</summary>
    public bool IsSelected(RailWindow window) => string.Equals(window.Id, SelectedWindowId, StringComparison.Ordinal);

    /// <summary>
    /// Whether <paramref name="row"/> wears the selection plate — one row at most, the thing in front. While the active
    /// character's own window (<c>main</c>) is in front that is the character's row, which already says it; any other
    /// window in front wears it itself, and the character row then does not, so two rows never claim to be selected.
    /// </summary>
    public bool Plated(RailCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return character.Active && !character.Windows.Any(w => !w.Own && IsSelected(w));
    }

    /// <inheritdoc cref="Plated(RailCharacter)"/>
    public bool Plated(RailWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return !window.Own && IsSelected(window);
    }

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
        && string.Equals(Cursor, other.Cursor, StringComparison.Ordinal)
        && string.Equals(SelectedWindowId, other.SelectedWindowId, StringComparison.Ordinal)
        && Worlds.Count == other.Worlds.Count
        && Worlds.Zip(other.Worlds).All(p => SameWorld(p.First, p.Second));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Collapsed);
        hash.Add(SelectedWindowId);
        hash.Add(Cursor);
        foreach (var world in Worlds)
        {
            hash.Add(world.Name);
            hash.Add(world.Characters.Count);
        }

        return hash.ToHashCode();
    }

    private static bool SameWorld(RailWorld a, RailWorld b) =>
        a.Name == b.Name && a.Host == b.Host && a.Port == b.Port && a.Accent.Equals(b.Accent) && Equals(a.Avatar, b.Avatar)
        && a.Characters.Count == b.Characters.Count
        && a.Characters.Zip(b.Characters).All(p => SameCharacter(p.First, p.Second));

    private static bool SameCharacter(RailCharacter a, RailCharacter b) =>
        a.Name == b.Name && a.SessionKey == b.SessionKey && a.Connected == b.Connected && a.Active == b.Active
        && a.Unread == b.Unread && a.Chord == b.Chord && a.InGroup == b.InGroup && Equals(a.Avatar, b.Avatar)
        && a.Windows.SequenceEqual(b.Windows); // RailWindow has no list member, so its record equality is by value
}
