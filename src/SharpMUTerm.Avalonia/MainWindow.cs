using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
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

    public MainWindow(WindowOptions options, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _options = options;
        _lifetime = lifetime;
        _config = ClientLaunch.LoadConfiguration(options, _notices);
        var (foreground, background) = WindowOptions.DefaultColours(_config);

        _driver = new AvaloniaConsoleDriver(options.Columns, options.Rows);
        _view = new TerminalView(_driver, foreground, background, options.FontPoints * 96f / 72f, options.FontFamily);

        Title = "SharpMUTerm";

        // No title bar: the client's own header is the top of the window, as it is the top of a terminal. The
        // border stays, and the header's blank cells move the window (WindowChrome). --title-bar brings it back.
        WindowDecorations = options.TitleBar ? WindowDecorations.Full : WindowDecorations.BorderOnly;
        Background = new SolidColorBrush(Color.FromRgb(background.Red, background.Green, background.Blue));
        Content = _view;
        var size = _view.SizeFor(options.Columns, options.Rows);
        Width = size.Width;
        Height = size.Height;

        Opened += (_, _) =>
        {
            _view.Focus();

            // After the first layout, so the driver already holds the window's real grid when the client
            // measures its chrome against it.
            Dispatcher.UIThread.Post(StartClient, DispatcherPriority.Background);
        };
        Closing += OnClosing;
    }

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
