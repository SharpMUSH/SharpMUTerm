using SharpMUTerm.Client.Composer;

namespace SharpMUTerm.Tui;

/// <summary>
/// A host that draws the composer with its own editor (the desktop window's native text editor) instead of cells: F1
/// as a full-screen editor in place of the cell modal, and each docked composer panel as an editor over its pane's
/// cells. The client hands it a <see cref="ComposerSession"/>; the host draws the session's <see cref="ComposerModel"/>
/// and hands every gesture back through it, so ⌃S, ⌥L, ⌥W and Esc end in the client methods the cell composer's keys
/// end in.
/// <para>
/// The docked editors arrive with the panels (<see cref="PanelOverlay.Composer"/>), placed like every native panel;
/// only F1, which has no pane, comes through here.
/// </para>
/// </summary>
internal interface INativeComposer
{
    /// <summary>
    /// Whether the host has somewhere to draw a composer now. False for a host built without a window (the window's
    /// <c>--render-png</c>, a test), which then gets the cell composer. Read on the client's thread.
    /// </summary>
    bool Available { get; }

    /// <summary>
    /// Shows F1 for <paramref name="session"/>, in <paramref name="ink"/>: the accent its title and keys wear, and the
    /// colour of the line under a misspelling. Called on the client's thread; the host marshals.
    /// </summary>
    void Open(ComposerSession session, PanelInk ink);

    /// <summary>Closes F1 if it is open, saying nothing back. Called on the client's thread.</summary>
    void Close();
}
