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

    /// <summary>Whether the open window is docked beside the output (its size is then the main window's half, not the player's).</summary>
    private bool _docked;

    /// <summary>The size the player last left the centred window at, kept for the run (never saved); null until they resize it.</summary>
    internal Size? RememberedSize { get; private set; }

    /// <summary>Called on the window's thread once the settings window has opened or closed, so the main window can re-hide or show what sits under it.</summary>
    /// <summary>The window's text sizes, read when the settings window opens (MainWindow sets it; designed sizes until then).</summary>
    public Func<NativeScales> Scales { get; set; } = () => NativeScales.One;

    public Action? OpenedOrClosed { get; set; }

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

        var window = new SettingsWindow(form, Scales().Dialogs);
        if (RememberedSize is { } remembered)
        {
            window.Width = remembered.Width;
            window.Height = remembered.Height;
        }

        Place(window, form);

        // The size the player resizes the centred window to is the size it opens at next time, for the run.
        window.Resized += (_, e) =>
        {
            if (ReferenceEquals(_window, window) && !_docked && window.WindowState == WindowState.Normal
                && e.ClientSize.Width > 0 && e.ClientSize.Height > 0)
            {
                RememberedSize = e.ClientSize;
            }
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
                Unveil();
                OpenedOrClosed?.Invoke();
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

        OpenedOrClosed?.Invoke();
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
            _docked = true;
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

        // Back from docked beside the output (or first opened): the player's size, centred over the main window.
        if (_docked || window.WindowStartupLocation == WindowStartupLocation.CenterOwner)
        {
            var wasDocked = _docked;
            _docked = false;
            var size = RememberedSize ?? new Size(SettingsWindow.DefaultWidth * window.Scale, SettingsWindow.DefaultHeight * window.Scale);

            // Inside the screen the main window is on (WindowFit): the default 1120 × 760 is 1,140 device rows at 150%.
            if ((owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary ?? owner.Screens.All.FirstOrDefault()) is { } screen)
            {
                size = WindowFit.Within(size, WindowFit.Room(screen.WorkingArea, screen.Scaling), new Size(window.MinWidth, window.MinHeight));
            }

            window.Width = Math.Max(window.MinWidth, size.Width);
            window.Height = Math.Max(window.MinHeight, size.Height);
            if (wasDocked)
            {
                var scale = owner.RenderScaling;
                var client = owner.ClientSize;
                window.Position = new PixelPoint(
                    owner.Position.X + (int)Math.Round(Math.Max(0, client.Width - window.Width) / 2 * scale),
                    owner.Position.Y + (int)Math.Round(Math.Max(0, client.Height - window.Height) / 2 * scale));
            }
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
