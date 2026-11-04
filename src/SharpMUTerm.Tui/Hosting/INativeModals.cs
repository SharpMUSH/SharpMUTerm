using SharpMUTerm.Client.Modals;

namespace SharpMUTerm.Tui;

/// <summary>
/// A host that draws the client's modals with its own controls (the desktop window's native modals) instead of cell
/// windows: the quit prompt, ⌃F search, ⌃R history, the ⌥M layouts list and the first-run welcome. The client hands it
/// a <see cref="ModalSession"/> built on the client's thread — one of <see cref="QuitSession"/>,
/// <see cref="SearchSession"/>, <see cref="HistorySession"/>, <see cref="LayoutsSession"/> or
/// <see cref="WelcomeSession"/> — and the host draws its model and hands every gesture back through it.
/// <para>
/// <b>One member for five modals</b>, because what they share is exactly what a host must get right once: where a
/// modal is drawn (over the cells, the native panels and the chrome), what is in front when two are open (the quit
/// prompt, always), where the keyboard goes when one closes, and which theme it wears. Five interfaces would be five
/// places to get the stacking wrong.
/// </para>
/// </summary>
internal interface INativeModals
{
    /// <summary>
    /// Whether the host has somewhere to draw a modal now. False for a host built without a window (the window's
    /// <c>--render-png</c>, the parity harness, a test), which then gets the cell modals. Read on the client's thread.
    /// </summary>
    bool Available { get; }

    /// <summary>
    /// Shows <paramref name="session"/>. A session that is <see cref="ModalSession.AlwaysInFront"/> (the quit prompt) is
    /// drawn over everything, including another modal, which stays open under it; any other replaces the modal open.
    /// Called on the client's thread; the host marshals.
    /// </summary>
    void Show(ModalSession session);

    /// <summary>Closes <paramref name="session"/> if it is shown, saying nothing back. Called on the client's thread.</summary>
    void Close(ModalSession session);
}
