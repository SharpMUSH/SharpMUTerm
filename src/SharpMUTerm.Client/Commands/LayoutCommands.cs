namespace SharpMUTerm.Client.Commands;

/// <summary>
/// The ids of the layout library's commands: the ⌥M list's actions, as commands, so any surface — the window's
/// native Layouts menu now, a package later — runs them through the registry instead of re-implementing them.
/// They act on the focused character, and on the layout it is using unless the id names one.
/// <para>
/// They are a family that generates no ⌃P entries: ⌥M is where a reader picks a layout from the keyboard, and
/// <c>Layouts</c> (<c>layout:layouts</c>) is the ⌃P entry that opens it.
/// </para>
/// </summary>
public static class LayoutCommands
{
    /// <summary>The prefix every layout-library id shares.</summary>
    public const string Prefix = "layout:lib:";

    /// <summary>Prefix of "apply this layout to the focused character's panes"; the remainder is the layout id.</summary>
    public const string ApplyPrefix = Prefix + "apply:";

    /// <summary>Saves the panes as they are into the layout in use (a built-in is saved as a copy).</summary>
    public const string SaveChanges = Prefix + "save";

    /// <summary>Saves the panes as they are as a new layout of the player's own.</summary>
    public const string SaveAsNew = Prefix + "save-new";

    /// <summary>Makes the layout in use the focused character's world's default.</summary>
    public const string UseForWorld = Prefix + "use-world";

    /// <summary>Makes the layout in use the focused character's own, whatever its world uses.</summary>
    public const string UseForCharacter = Prefix + "use-character";

    /// <summary>The id that applies <paramref name="layoutId"/>.</summary>
    public static string Apply(string layoutId) => ApplyPrefix + layoutId;
}
