using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window: a <see cref="TerminalView"/> filling it, and the client running on a thread of its own. The
/// client's loop blocks the thread it runs on (<c>ConsoleWindowSystem.Run</c>), and Avalonia must keep the
/// main one, so the two loops talk only through the driver.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly WindowOptions _options;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly AvaloniaConsoleDriver _driver;
    private readonly TerminalView _view;
    private readonly AppConfiguration _config;
    private readonly List<string> _notices = [];
    private bool _clientEnded;
    private bool _started;
    private Size _normalSize;
    private long _stateChangedAt;

    public MainWindow(WindowOptions options, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _options = options;
        _lifetime = lifetime;
        _config = ClientLaunch.LoadConfiguration(options, _notices);
        var (foreground, background) = WindowOptions.DefaultColours(_config);

        _driver = new AvaloniaConsoleDriver(options.Columns, options.Rows);
        _view = new TerminalView(_driver, foreground, background, options.FontPoints * 96f / 72f, options.FontFamily);

        Title = "SharpMUTerm";

        // The window's own pop-ups (tooltips, the right-click menu) follow the client's theme: a light menu
        // over a dark client reads as somebody else's.
        if (Application.Current is { } app)
        {
            var luma = (0.2126 * background.Red) + (0.7152 * background.Green) + (0.0722 * background.Blue);
            app.RequestedThemeVariant = luma < 128 ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        // No title bar: the client's own header is the top of the window, as it is the top of a terminal. The
        // border stays, and the header's blank cells move the window (WindowChrome). --title-bar brings it back.
        WindowDecorations = options.TitleBar ? WindowDecorations.Full : WindowDecorations.BorderOnly;
        Background = new SolidColorBrush(Color.FromRgb(background.Red, background.Green, background.Blue));
        Content = _view;
        var size = _view.SizeFor(options.Columns, options.Rows);
        Width = size.Width;
        Height = size.Height;
        _normalSize = size;

        Opened += (_, _) =>
        {
            _view.Focus();

            // After the first layout, so the driver already holds the window's real grid when the client
            // measures its chrome against it.
            Dispatcher.UIThread.Post(StartClient, DispatcherPriority.Background);
        };
        Closing += OnClosing;
    }

    /// <summary>
    /// Restoring a window that has no title bar puts it back at the size it had, which Avalonia does not always
    /// do on its own. Without a title bar the border is drawn by Avalonia (on X11, at least) and is part of the
    /// window only while it is not maximised; when a restore is laid out before the window manager has sent the
    /// restored size, the maximised <see cref="Layoutable.Width"/> is pushed back to the platform with the border
    /// added, and the window "restores" to a size larger than the screen. So the last size the window had while
    /// it was not maximised is remembered here, and restored by hand.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            _stateChangedAt = Environment.TickCount64;
            if (change.GetNewValue<WindowState>() == WindowState.Normal && _normalSize is { Width: > 0, Height: > 0 })
            {
                Width = _normalSize.Width;
                Height = _normalSize.Height;
            }
        }
        else if (change.Property == ClientSizeProperty
                 && WindowState == WindowState.Normal
                 && Environment.TickCount64 - _stateChangedAt > SettleMilliseconds)
        {
            // Not while a change of state is still settling: the sizes reported then are the maximised one on its
            // way out, or the oversized one this exists to undo.
            _normalSize = change.GetNewValue<Size>();
        }
    }

    private const int SettleMilliseconds = 500;

    private void StartClient()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        var thread = new Thread(() =>
        {
            var code = 1;
            try
            {
                code = ClientLaunch.Run(_options, _config, _notices, _driver, CopyToClipboard);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }

            Dispatcher.UIThread.Post(() =>
            {
                _clientEnded = true;
                Close();
                _lifetime.Shutdown(code);
            });
        })
        {
            IsBackground = true,
            Name = "SharpMUTerm client",
        };
        thread.Start();
    }

    /// <summary>
    /// Closing the window is asking to quit, and the client already has a way to ask that: ⌃Q, which opens its
    /// confirmation. So the close is refused and the chord sent — a connected session is not dropped by a
    /// stray click on the title bar, and the workspace is saved on the way out as it is from a terminal.
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_clientEnded || !_started)
        {
            return;
        }

        e.Cancel = true;
        _driver.RaiseKey(new ConsoleKeyInfo('\x11', ConsoleKey.Q, false, false, true));
    }

    /// <summary>The client's copy, on the window's clipboard. Called from the client's thread.</summary>
    private void CopyToClipboard(string text) =>
        Dispatcher.UIThread.Post(async () =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        });
}
