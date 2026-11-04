using Avalonia.Controls;
using Avalonia.Threading;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window host's <see cref="INativeSettings"/>: one <see cref="SettingsWindow"/> at a time, owned by the main
/// window. The client calls in on its own thread, so everything here is posted to the window's.
/// </summary>
internal sealed class SettingsPresenter : INativeSettings
{
    private SettingsWindow? _window;

    /// <summary>The window the settings window belongs to, set once the main window exists.</summary>
    public Window? Owner { get; set; }

    /// <summary>The open settings window, for a test; read on the window's thread.</summary>
    public SettingsWindow? Window => _window;

    public void Show(SettingsForm form) => Dispatcher.UIThread.Post(() => ShowNow(form));

    public void Close() => Dispatcher.UIThread.Post(() => _window?.Close());

    /// <summary>Shows <paramref name="form"/> now; on the window's thread.</summary>
    internal SettingsWindow ShowNow(SettingsForm form)
    {
        if (_window is { } open)
        {
            open.Load(form);
            open.Activate();
            return open;
        }

        var window = new SettingsWindow(form);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }

            // The keyboard goes back to the client, as it does when a cell screen closes.
            Owner?.Activate();
        };
        _window = window;
        if (Owner is { IsVisible: true } owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }

        return window;
    }
}
