using Avalonia.Controls;
using Avalonia.Threading;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window host's <see cref="INativeComposer"/>: F1 as a <see cref="ComposerOverlay"/> over the cell area, under
/// the desktop layer (so the toasts are drawn over it) and over the cells. The client calls in on its own thread, so
/// everything here is posted to the window's. A host never attached — <c>--render-png</c>, a test that builds no
/// window — is not <see cref="Available"/>, and the client opens its cell composer instead.
/// </summary>
internal sealed class ComposerPresenter : INativeComposer
{
    private volatile bool _attached;
    private Panel? _root;
    private Control? _below;
    private TerminalView? _view;
    private Action? _refocus;
    private ComposerOverlay? _overlay;

    /// <summary>The open F1, for a test; read on the window's thread.</summary>
    public ComposerOverlay? Overlay => _overlay;

    public bool Available => _attached;

    /// <summary>
    /// Where F1 is drawn: in <paramref name="root"/>, just under <paramref name="below"/> (the desktop layer), over the
    /// cells of <paramref name="view"/>; and what takes the keyboard back when it closes. On the window's thread.
    /// </summary>
    public void Attach(Panel root, Control below, TerminalView view, Action refocus)
    {
        _root = root;
        _below = below;
        _view = view;
        _refocus = refocus;
        _attached = true;
    }

    public void Open(ComposerSession session, PanelInk ink) => Dispatcher.UIThread.Post(() => OpenNow(session, ink));

    public void Close() => Dispatcher.UIThread.Post(CloseNow);

    /// <summary>Shows F1 for <paramref name="session"/> now, replacing one already open; on the window's thread.</summary>
    internal ComposerOverlay? OpenNow(ComposerSession session, PanelInk ink)
    {
        if (_overlay is { } open)
        {
            Remove(open, refocus: false);
        }

        if (_root is not { } root || _view is not { } view || session.IsEnded)
        {
            return null;
        }

        var overlay = new ComposerOverlay(view, session, ink);
        _overlay = overlay;
        var at = _below is not null ? root.Children.IndexOf(_below) : -1;
        root.Children.Insert(at < 0 ? root.Children.Count : at, overlay);
        overlay.Composer.Editor.Focus();
        return overlay;
    }

    /// <summary>Takes F1 down now; on the window's thread.</summary>
    internal void CloseNow()
    {
        if (_overlay is { } open)
        {
            Remove(open, refocus: true);
        }
    }

    private void Remove(ComposerOverlay overlay, bool refocus)
    {
        if (!ReferenceEquals(_overlay, overlay))
        {
            return;
        }

        _overlay = null;
        overlay.Detach();
        _root?.Children.Remove(overlay);
        if (refocus)
        {
            _refocus?.Invoke();
        }
    }
}
