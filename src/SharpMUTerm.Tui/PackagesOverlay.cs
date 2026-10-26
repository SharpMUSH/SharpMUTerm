using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpMUTerm.Core.Packages;

namespace SharpMUTerm.Tui;

/// <summary>
/// The two package surfaces, as one modal window: the review before an install (⏎ installs, Esc cancels)
/// and the Packages screen (F10, ⌃P ▸ Packages). What they say is <see cref="PackageScreen"/>'s; what a key
/// does to the installed set is the app's, through the callbacks.
/// </summary>
internal sealed class PackagesOverlay
{
    private readonly ConsoleWindowSystem _system;

    private Window? _window;
    private MarkupControl? _content;
    private Func<int, List<string>>? _render;
    private Func<ConsoleKeyInfo, bool>? _key;

    public PackagesOverlay(ConsoleWindowSystem system)
    {
        _system = system;
    }

    public bool IsOpen => _window is not null;

    /// <summary>Which surface is up: <c>review</c>, <c>list</c> or null.</summary>
    public string? Mode { get; private set; }

    /// <summary>What the surface shows now, for a test to read.</summary>
    internal IReadOnlyList<string> Lines => _render?.Invoke(Width()) ?? [];

    /// <summary>
    /// Opens a surface. <paramref name="render"/> draws it at a width; <paramref name="key"/> answers a key
    /// and returns whether the surface stays open.
    /// </summary>
    public void Open(string mode, string title, Func<int, List<string>> render, Func<ConsoleKeyInfo, bool> key)
    {
        if (_window is not null)
        {
            Close();
        }

        _render = render;
        _key = key;
        var desktop = _system.DesktopDimensions;
        var width = Math.Clamp(desktop.Width - 8, 44, 110);
        var lines = render(width - 4);
        var height = Math.Clamp(lines.Count + 2, 8, Math.Max(8, desktop.Height - 2));
        _content = new MarkupControl(lines);

        // Centered() after WithSize(), for CommandPalette's reason.
        Window? built = null;
        built = new WindowBuilder(_system)
            .WithTitle(title)
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBackgroundColor(new Color(ScreenPalette.MenuBg))
            .HideTitleButtons()
            .Resizable(false)
            .WithSize(width, height)
            .Centered()
            .AddControl(_content)
            .OnClosed((_, _) =>
            {
                // A window closed because the next surface replaced it must not clear the next one's state.
                if (_window == built)
                {
                    Reset();
                }
            })
            .Build();
        _window = built;

        _window.PreviewKeyPressed += OnKey;
        _system.AddWindow(_window);

        // Last, so a test (or anything else) that waits on the mode finds the window already up.
        Mode = mode;
    }

    public void Repaint()
    {
        if (_render is not null && _content is not null)
        {
            _content.SetContent(_render(Width()));
            _window?.Invalidate(redrawAll: true);
        }
    }

    internal void SimulateKey(ConsoleKeyInfo key) => OnKey(this, new KeyPressedEventArgs(key, false));

    public void Close()
    {
        if (_window is { } window)
        {
            _system.CloseModalWindow(window);
        }
    }

    private int Width() => Math.Max(20, (_window?.Width ?? 80) - 4);

    private void OnKey(object? sender, KeyPressedEventArgs e)
    {
        if (_window is null || _key is null)
        {
            return;
        }

        e.Handled = true;

        // A key can start a fetch whose answer opens the next surface (browse, then a review). Only the
        // surface the key was pressed on is closed, so that answer is never closed with it.
        var window = _window;
        if (e.KeyInfo.Key == ConsoleKey.Escape || !_key(e.KeyInfo))
        {
            if (_window == window)
            {
                Close();
            }

            return;
        }

        Repaint();
    }

    private void Reset()
    {
        _window = null;
        _content = null;
        _render = null;
        _key = null;
        Mode = null;
    }
}
