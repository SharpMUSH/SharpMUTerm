using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// F1 drawn by the window: a <see cref="ComposerView"/> over the whole cell area, in the theme's own colours, in place
/// of the cell modal. The native header and sidebar stay where they are (inert while it is up, as under any modal);
/// the toasts are drawn over it, so a refusal is still seen. It follows its <see cref="ComposerSession"/>: the client
/// publishes a new model when the escaping flips or a word is added, and that is all it ever redraws from.
/// </summary>
internal sealed class ComposerOverlay : Border
{
    private readonly TerminalView _view;
    private readonly ComposerSession _session;
    private readonly PanelInk _ink;
    private readonly ComposerView _composer;

    public ComposerOverlay(TerminalView view, ComposerSession session, PanelInk ink)
    {
        _view = view;
        _session = session;
        _ink = ink;
        Focusable = false;
        Margin = view.Margin;
        _composer = new ComposerView(view, fullScreen: true);
        Child = _composer;
        _session.Changed += OnChanged;
        _view.PropertyChanged += OnViewChanged;
        Show(_session.Model);
    }

    /// <summary>The session it draws. Internal for tests.</summary>
    internal ComposerSession Session => _session;

    /// <summary>The editor and its bands. Internal for tests.</summary>
    internal ComposerView Composer => _composer;

    /// <summary>Stops following the session and the view; the presenter calls it when the overlay goes.</summary>
    internal void Detach()
    {
        _session.Changed -= OnChanged;
        _view.PropertyChanged -= OnViewChanged;
    }

    private void OnChanged(ComposerModel model) => Dispatcher.UIThread.Post(() => Show(model));

    private void OnViewChanged(object? sender, global::Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == MarginProperty)
        {
            Margin = _view.Margin;
        }
    }

    private void Show(ComposerModel model)
    {
        var plane = DesktopLayer.Colour(_view.Renderer.DefaultBackground);
        var text = DesktopLayer.Colour(_view.Renderer.DefaultForeground);
        Background = new SolidColorBrush(plane);
        _composer.Show(model, _session, _ink, plane, text);
    }
}
