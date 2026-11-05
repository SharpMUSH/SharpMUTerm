namespace SharpMUTerm.Tui;

/// <summary>
/// A host that can ask the player for a file with the desktop's own picker (the window). The terminal has none — a
/// path is typed after <c>/import</c> there — and neither have the snapshot, the tests and <c>--plain</c>.
/// </summary>
internal interface IFilePicker
{
    /// <summary>Whether the picker can be shown now (false until the window that owns it exists). Read on the client's thread.</summary>
    bool Available { get; }

    /// <summary>
    /// Shows the picker titled <paramref name="title"/>; <paramref name="picked"/> is told the local path chosen, or null
    /// when the player cancelled, from the host's thread (the client marshals). Called on the client's thread.
    /// </summary>
    void Pick(string title, Action<string?> picked);
}
