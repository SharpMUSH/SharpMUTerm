using Avalonia.Controls;
using Avalonia.Threading;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window host's <see cref="INativePalette"/>: one <see cref="PaletteOverlay"/> at a time, added as the last child
/// of the window's root panel so it lies over the cells, the native panels and the chrome. The client calls in on its
/// own thread, so everything here is posted to the window's. When the palette goes, the keyboard goes back to the
/// cells — which is the client's command line, since the client pins its focus there.
/// </summary>
internal sealed class PalettePresenter : INativePalette
{
    private Panel? _host;
    private Action? _refocus;
    private PaletteOverlay? _overlay;

    /// <summary>The open palette, for a test; read on the window's thread.</summary>
    public PaletteOverlay? Overlay => _overlay;

    /// <summary>
    /// Where the palette is drawn, and what takes the keyboard back when it closes. Set once by the main window (or a
    /// test), on the window's thread.
    /// </summary>
    public void Attach(Panel host, Action refocus)
    {
        _host = host;
        _refocus = refocus;
    }

    public void Show(PaletteSession session) => Dispatcher.UIThread.Post(() => ShowNow(session));

    public void Close() => Dispatcher.UIThread.Post(() =>
    {
        if (_overlay is { } open)
        {
            Remove(open);
        }
    });

    /// <summary>Shows <paramref name="session"/> now, replacing a palette already open; on the window's thread.</summary>
    internal PaletteOverlay? ShowNow(PaletteSession session)
    {
        if (_overlay is { } open)
        {
            Remove(open);
            open.Session.Dismiss();
        }

        if (_host is not { } host)
        {
            // Nowhere to draw it: say it closed, so the client is not left thinking a palette is up.
            session.Dismiss();
            return null;
        }

        var overlay = new PaletteOverlay(session, Remove);
        _overlay = overlay;
        host.Children.Add(overlay);
        overlay.SearchBox.Focus();
        return overlay;
    }

    private void Remove(PaletteOverlay overlay)
    {
        if (!ReferenceEquals(_overlay, overlay))
        {
            return;
        }

        _overlay = null;
        _host?.Children.Remove(overlay);
        _refocus?.Invoke();
    }
}
