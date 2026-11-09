using Avalonia;
using Avalonia.Automation;
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
    private readonly DesktopLayer _layer;
    private readonly IBrush _opaque;
    private readonly AppConfiguration _config;
    private readonly List<string> _notices = [];
    private bool _clientEnded;
    private bool _started;
    private Size _normalSize;
    private long _stateChangedAt;

    // Windowed full screen is a Normal window with no decorations at the screen's bounds, so it is a flag of ours, not a
    // WindowState; what it covered is put back when it ends.
    private bool _windowedFullScreen;
    private PixelPoint _beforeWindowedPosition;
    private Size _beforeWindowedSize;

    // What a full screen goes back to when it ends: windowed or maximised.
    private WindowMode _beforeFullScreen = WindowMode.Windowed;

    public MainWindow(WindowOptions options, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _options = options;
        _lifetime = lifetime;
        NativeTokens.ApplyTextRendering(this);
        _config = ClientLaunch.LoadConfiguration(options, _notices);
        var (foreground, background) = WindowOptions.DefaultColours(_config);

        _driver = new AvaloniaConsoleDriver(options.Columns, options.Rows);
        _view = new TerminalView(_driver, foreground, background, options.FontPoints * 96f / 72f, options.FontFamily, options.LineHeight);

        _view.AttachZoomKeys(this);

        Title = SharpMUTerm.Client.Chrome.WindowTitle.Product;
        AppIcon.Apply(this);

        // Character@World — SharpMUTerm for what is in front, from the client's own header (WindowTitle).
        _driver.TitleBar.Changed += title => Dispatcher.UIThread.Post(() => Title = title);

        // Whether the window is in front decides whether an alert is shown at all, and the title's unread count.
        // Activation also takes back an X11 urgency hint the alert set (DesktopAlert).
        var alert = DesktopAlert.ForThisMachine();
        Activated += (_, _) =>
        {
            _driver.Attention.SetFocused(true);
            alert.Clear();
        };
        Deactivated += (_, _) => _driver.Attention.SetFocused(false);
        _driver.Attention.Requested += request => Dispatcher.UIThread.Post(() =>
        {
            if (!IsActive)
            {
                var handle = TryGetPlatformHandle();
                alert.Show(request, handle?.Handle, handle?.HandleDescriptor);
            }
        });
        _driver.Settings.Owner = this;

        // The window's own pop-ups (tooltips, the right-click menu) follow the client's theme: a light menu
        // over a dark client reads as somebody else's.
        if (Application.Current is { } app)
        {
            var luma = (0.2126 * background.Red) + (0.7152 * background.Green) + (0.0722 * background.Blue);
            app.RequestedThemeVariant = luma < 128 ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        // No title bar: the header is the top of the window — the window's own (NativeHeader), or the client's cells
        // with --cell-chrome. The header's empty space moves the window and its edges resize it (WindowChrome).
        // --title-bar brings it back.
        _decorations = DecorationsFor(options.TitleBar);
        WindowDecorations = _decorations;
        _opaque = new SolidColorBrush(Color.FromRgb(background.Red, background.Green, background.Blue));
        Background = _opaque;
        // The view fills the window past the native chrome; the screen-reader regions sit under it, unseen.
        var root = new Panel();
        foreach (var region in _driver.Announcer.Regions())
        {
            root.Children.Add(region);
        }

        root.Children.Add(_view);

        // The window's native views: the header band and the connections sidebar beside the cells (the layer moves the
        // view out of their way, so no cell lies under either), the Layouts button, and native panels over their panes.
        var layer = new DesktopLayer(_view, options.Chrome);
        _layer = layer;
        root.Children.Add(layer);

        // The edge Avalonia would have drawn round a BorderOnly window on X11, drawn here (DecorationsFor): a hairline,
        // only while the window is an ordinary window.
        if (_decorations == WindowDecorations.None)
        {
            _edge = new Border
            {
                BorderBrush = new SolidColorBrush(NativeTokens.For(_config.Theme).Edge),
                BorderThickness = new Thickness(NativeTokens.Hairline),
                IsHitTestVisible = false,
            };
            root.Children.Add(_edge);
        }

        // F7's background transparency: the client says the level, the window asks the desktop for a translucent surface
        // and draws its planes at that level once it has one (SyncTranslucency).
        _driver.Translucency.Changed += _ => Dispatcher.UIThread.Post(ApplyTranslucency);

        // F1, over the cells and under the layer, so a toast is still seen over it. Closing it gives the keyboard to an
        // armed docked composer if there is one, else to the cells.
        _driver.Composer.Attach(root, layer, _view, () => layer.FocusArmedComposer());

        // The settings window takes the keyboard when it opens and hands it back here when it closes.
        _driver.Settings.Refocus = () => layer.FocusArmedComposer();

        // ⌃P, over everything above; it is added last when it opens, and gives the keyboard back to the cells.
        _driver.Palette.Attach(root, () => layer.FocusArmedComposer());

        // The modals, over everything; the quit prompt is kept the last child whatever is added after it, and brings
        // this window forward. ⌃Q from any other modal goes to the client, which opens the prompt over it.
        _driver.Modals.Attach(this, root, () => layer.FocusArmedComposer(), key => _driver.RaiseKey(key));

        // No toast while settings, ⌃P or a modal is in front of the cells (F1 keeps them: it sits under the layer).
        layer.ToastsHidden = () => _driver.Settings.Window is not null || _driver.Palette.Overlay is not null
            || _driver.Modals.Modal is not null || _driver.Modals.Quit is not null;

        // The settings window is a window of its own, so opening or closing it paints no frame here: re-check at once, or
        // a toast already up stays beside it (seen under Xvfb, where nothing stacks the windows) and one hidden under it
        // stays hidden after it closes.
        _driver.Settings.OpenedOrClosed = layer.Sync;

        // Panels popped out into windows of their own, owned by this one so they stay above it and go with it.
        _driver.Floating.Attach(this, _view);
        _driver.FilePicker.Attach(this);
        _driver.WindowModes.Attach(this);
        Closed += (_, _) => _driver.Floating.Detach();
        Content = root;
        AutomationProperties.SetName(_view, "SharpMUTerm");

        // --size names the grid, so the window is that grid plus the native chrome around it — measured at the scale
        // of the screen it opens on, and shrunk by whole cells to that screen's working area (WindowFit): at 150% on a
        // 1080p screen the default grid is taller than the screen, and its command line opened below the edge.
        var insets = NativeLayout.Insets(options.Chrome, collapsed: false);
        var screen = Screens is { } screens ? screens.Primary ?? screens.All.FirstOrDefault() : null;
        if (screen is not null)
        {
            _view.PrepareScale(screen.Scaling);
        }

        var cells = _view.SizeFor(options.Columns, options.Rows);
        var size = new Size(cells.Width + insets.Left + insets.Right, cells.Height + insets.Top + insets.Bottom);
        if (screen is not null)
        {
            var chrome = new Size(insets.Left + insets.Right, insets.Top + insets.Bottom + _view.SizeFor(0, 0).Height);
            var fitted = WindowFit.InCells(size, WindowFit.Room(screen.WorkingArea, screen.Scaling), _view.CellExtent, chrome);
            if (fitted != size)
            {
                size = fitted;
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = WindowFit.Centre(size, screen.WorkingArea, screen.Scaling);
            }
        }

        Width = size.Width;
        Height = size.Height;
        _normalSize = size;

        Opened += (_, _) =>
        {
            _view.Focus();

            // How the configuration says the window opens (the Window settings page). Windowed is how it already is.
            if (_config.WindowStart != WindowMode.Windowed)
            {
                SetMode(_config.WindowStart);
            }

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
    /// <summary>
    /// Asks the desktop for a translucent window while F7's background transparency is above zero, and for an opaque one
    /// otherwise. What the desktop gives is <see cref="TopLevel.ActualTransparencyLevel"/>, which may arrive later and may
    /// be none at all (an X server with no compositor), so the planes follow it rather than the request.
    /// </summary>
    private void ApplyTranslucency()
    {
        TransparencyLevelHint = _driver.Translucency.Percent > 0 ? [WindowTransparencyLevel.Transparent] : [];
        SyncTranslucency();
    }

    /// <summary>The planes at the level the desktop can show: opaque, on the theme's own colour, until it can show any.</summary>
    private void SyncTranslucency()
    {
        var alpha = WindowTranslucency.PlaneAlpha(_driver.Translucency.Percent, ActualTransparencyLevel);
        Background = alpha == 255 ? _opaque : Brushes.Transparent;

        // Subpixel text blends its fringes against what is under it, and under a translucent plane that is the desktop
        // the window cannot see: greyscale while the planes are translucent, the default otherwise.
        TextOptions.SetTextRenderingMode(this, alpha == 255 ? TextRenderingMode.Unspecified : TextRenderingMode.Antialias);

        _layer.PlaneAlpha = alpha;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActualTransparencyLevelProperty && _layer is not null)
        {
            SyncTranslucency();
        }
        else if (change.Property == WindowStateProperty)
        {
            _stateChangedAt = Environment.TickCount64;
            var state = change.GetNewValue<WindowState>();

            // The window manager maximised or minimised a windowed full screen (a keyboard shortcut, a snap): it is
            // not one any more, and gets its frame back.
            if (_windowedFullScreen && state is WindowState.Maximized or WindowState.FullScreen)
            {
                _windowedFullScreen = false;
                WindowDecorations = _decorations;
            }

            if (state == WindowState.Normal && !_windowedFullScreen && _normalSize is { Width: > 0, Height: > 0 })
            {
                Width = _normalSize.Width;
                Height = _normalSize.Height;
            }

            NoteMode();
        }
        else if (change.Property == ClientSizeProperty
                 && WindowState == WindowState.Normal
                 && !_windowedFullScreen
                 && Environment.TickCount64 - _stateChangedAt > SettleMilliseconds)
        {
            // Not while a change of state is still settling: the sizes reported then are the maximised one on its
            // way out, or the oversized one this exists to undo.
            _normalSize = change.GetNewValue<Size>();
        }
    }

    private const int SettleMilliseconds = 500;

    private readonly WindowDecorations _decorations;
    private readonly Border? _edge;

    /// <summary>
    /// The decorations of an ordinary window. <c>--title-bar</c> asks for the platform's whole frame. Otherwise none on
    /// Linux and a border elsewhere. On X11 — which is also every Wayland desktop, through XWayland — a BorderOnly window
    /// has no frame from the window manager and Avalonia draws its own instead, with a drop shadow about nine pixels deep
    /// <em>inside</em> the window. The window manager cannot tell that margin from the window (Avalonia publishes no
    /// <c>_GTK_FRAME_EXTENTS</c>), so the window's content stopped short of every edge of what the desktop thought was the
    /// window: a gap round a window snapped or tiled to the screen, and a transparent or black frame where the compositor
    /// shows no alpha. With none the content is the whole window; its edge is a hairline drawn here, and resizing was
    /// already ours (<see cref="WindowChrome"/>). Windows draws BorderOnly natively, with the shadow outside the window.
    /// </summary>
    internal static WindowDecorations DecorationsFor(bool titleBar) =>
        titleBar ? WindowDecorations.Full
        : OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD() ? WindowDecorations.None
        : WindowDecorations.BorderOnly;

    /// <summary>The mode the window is in now.</summary>
    internal WindowMode Mode =>
        _windowedFullScreen ? WindowMode.WindowedFullScreen
        : WindowState switch
        {
            WindowState.FullScreen => WindowMode.FullScreen,
            WindowState.Maximized => WindowMode.Maximised,
            _ => WindowMode.Windowed,
        };

    /// <summary>
    /// Whether the window is covering the screen (full screen or windowed full screen), where its edges do not resize it
    /// and its header does not move it.
    /// </summary>
    internal bool CoversScreen => Mode is WindowMode.FullScreen or WindowMode.WindowedFullScreen;

    /// <summary>
    /// Goes into <paramref name="mode"/>, or — when the window is already in it — back to the windowed or maximised state
    /// it was in before. F11 and ⇧F11, and the client's toggles, end here.
    /// </summary>
    internal void ToggleMode(WindowMode mode) =>
        SetMode(Mode == mode && mode is WindowMode.FullScreen or WindowMode.WindowedFullScreen ? _beforeFullScreen : mode);

    /// <summary>⌥⏎ and a double-click on the header: out of a full screen first, else maximise and restore.</summary>
    internal void ToggleMaximised() =>
        SetMode(CoversScreen ? _beforeFullScreen : Mode == WindowMode.Maximised ? WindowMode.Windowed : WindowMode.Maximised);

    /// <summary>Puts the window in <paramref name="mode"/>.</summary>
    internal void SetMode(WindowMode mode)
    {
        var current = Mode;
        if (mode == current)
        {
            return;
        }

        if (current is WindowMode.Windowed or WindowMode.Maximised && mode is WindowMode.FullScreen or WindowMode.WindowedFullScreen)
        {
            _beforeFullScreen = current;
        }

        if (_windowedFullScreen)
        {
            LeaveWindowedFullScreen(mode);
        }

        switch (mode)
        {
            case WindowMode.Windowed:
                WindowState = WindowState.Normal;
                break;
            case WindowMode.Maximised:
                WindowState = WindowState.Maximized;
                break;
            case WindowMode.FullScreen:
                WindowState = WindowState.FullScreen;
                break;
            case WindowMode.WindowedFullScreen:
                EnterWindowedFullScreen();
                break;
        }

        NoteMode();
    }

    /// <summary>
    /// A Normal window with no decorations at the bounds of the screen it is on — not its working area, so it covers the
    /// taskbar — and nothing else: it stays an ordinary window, which is the point of it.
    /// </summary>
    private void EnterWindowedFullScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null)
        {
            return;
        }

        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        _beforeWindowedPosition = Position;
        _beforeWindowedSize = _normalSize is { Width: > 0, Height: > 0 } ? _normalSize : ClientSize;
        _windowedFullScreen = true;
        WindowDecorations = WindowDecorations.None;
        var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width / scale;
        Height = screen.Bounds.Height / scale;
    }

    private void LeaveWindowedFullScreen(WindowMode next)
    {
        _windowedFullScreen = false;
        WindowDecorations = _decorations;
        if (next == WindowMode.Windowed)
        {
            Position = _beforeWindowedPosition;
            Width = _beforeWindowedSize.Width;
            Height = _beforeWindowedSize.Height;
            _normalSize = _beforeWindowedSize;
        }
    }

    private void NoteMode()
    {
        var mode = Mode;
        if (_edge is not null)
        {
            _edge.IsVisible = mode == WindowMode.Windowed;
        }

        _driver.WindowModes.Note(mode);
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
