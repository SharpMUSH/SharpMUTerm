using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window host's <see cref="INativeSettings"/>: one <see cref="SettingsWindow"/> at a time, owned by the main
/// window. The client calls in on its own thread, so everything here is posted to the window's.
/// <para>
/// It is drawn as the palette's modal: its own frame, Esc and a close button to leave, and the main window under a
/// scrim (a press on it closes the settings, as a press on the palette's scrim does). Opened from the trigger builder
/// (<see cref="SettingsForm.BesideOutput"/>) it is docked to the right half of the main window instead, with no scrim,
/// so the line the rule was made from stays in view beside the rule's live test.
/// </para>
/// </summary>
internal sealed class SettingsPresenter : INativeSettings
{
    private SettingsWindow? _window;
    private Border? _scrim;

    /// <summary>The window the settings window belongs to, set once the main window exists.</summary>
    public Window? Owner { get; set; }

    /// <summary>Gives the keyboard back to the client's command line (or an armed docked composer) once the window closes.</summary>
    public Action? Refocus { get; set; }

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
            Place(open, form);
            open.FocusFirst();
            return open;
        }

        var window = new SettingsWindow(form);
        Place(window, form);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
                Unveil();
            }

            // The keyboard goes back to the client, as it does when a cell screen closes.
            Owner?.Activate();
            Refocus?.Invoke();
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

    /// <summary>The scrim over the main window while the settings are open, for a test; null when there is none.</summary>
    internal Border? Scrim => _scrim;

    /// <summary>
    /// Centres <paramref name="window"/> over the main window under a scrim, or — opened beside the output — docks it to
    /// the main window's right half with none.
    /// </summary>
    private void Place(SettingsWindow window, SettingsForm form)
    {
        if (Owner is not { IsVisible: true } owner)
        {
            return;
        }

        if (form.BesideOutput)
        {
            Unveil();
            var scale = owner.RenderScaling;
            var size = owner.ClientSize;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Width = Math.Max(window.MinWidth, size.Width / 2);
            window.Height = Math.Max(window.MinHeight, size.Height);
            window.Position = new PixelPoint(
                owner.Position.X + (int)Math.Round((size.Width - window.Width) * scale),
                owner.Position.Y);
            return;
        }

        if (_scrim is null && owner.Content is Panel root)
        {
            var tokens = window.Tokens;
            _scrim = new Border { Background = tokens.Brush(tokens.Scrim), Focusable = false };
            _scrim.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                _window?.Close();
            };
            root.Children.Add(_scrim);
        }
    }

    /// <summary>Takes the scrim off the main window.</summary>
    private void Unveil()
    {
        if (_scrim is { Parent: Panel root } scrim)
        {
            root.Children.Remove(scrim);
        }

        _scrim = null;
    }
}
