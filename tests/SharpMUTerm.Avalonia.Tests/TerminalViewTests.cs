using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Themes.Simple;
using Color = SharpConsoleUI.Color;
using Window = Avalonia.Controls.Window;
using SharpConsoleUI.Drivers;
using SkiaSharp;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The control in a real (headless) Avalonia window with the Skia renderer: what reaches the driver when keys
/// and the pointer are used, and what the compositor actually paints from a published frame.
/// </summary>
public class TerminalViewTests
{
    private static readonly SKColor Back = new(0x10, 0x20, 0x30);

    [Test]
    public async Task KeysReachTheDriverInTheParsersSpelling()
    {
        var keys = await Run(window =>
        {
            var (_, driver) = ((TerminalView, AvaloniaConsoleDriver))window.Tag!;
            var seen = new List<ConsoleKeyInfo>();
            driver.KeyPressed += (_, k) => seen.Add(k);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            window.KeyTextInput("é");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            return seen;
        });

        await Assert.That(keys).IsEquivalentTo(new[]
        {
            new ConsoleKeyInfo('\x01', ConsoleKey.A, false, false, true),
            new ConsoleKeyInfo('é', ConsoleKey.NoName, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
        });
    }

    [Test]
    public async Task AClickReachesTheDriverAsAClickInTheRightCell()
    {
        var events = await Run(window =>
        {
            var (view, driver) = ((TerminalView, AvaloniaConsoleDriver))window.Tag!;
            var seen = new List<(List<MouseFlags> Flags, System.Drawing.Point Cell)>();
            driver.MouseEvent += (_, flags, cell) => seen.Add((flags, cell));
            var cell = view.SizeFor(1, 1);
            var at = new Point((cell.Width * 4) + 1, (cell.Height * 2) + 1);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            return seen;
        });

        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events[0].Flags).IsEquivalentTo(new[] { MouseFlags.Button1Pressed });
        await Assert.That(events[1].Flags).IsEquivalentTo(new[] { MouseFlags.Button1Released, MouseFlags.Button1Clicked });
        await Assert.That(events[1].Cell).IsEqualTo(new System.Drawing.Point(4, 2));
    }

    [Test]
    public async Task APublishedFrameIsWhatTheWindowPaints()
    {
        var colours = await Run(window =>
        {
            var (view, driver) = ((TerminalView, AvaloniaConsoleDriver))window.Tag!;
            driver.FillCells(0, 0, 3, ' ', Color.Default, new Color(255, 0, 0));
            driver.Flush();
            Dispatch.Drain();
            using var frame = window.CaptureRenderedFrame()!;
            var cell = view.SizeFor(1, 1);
            global::Avalonia.Media.Color At(double x, double y) => Pixel(frame, x, y);
            return (Inside: At(cell.Width * 1.5, cell.Height / 2), Outside: At(cell.Width * 5.5, cell.Height / 2));
        });

        await Assert.That(colours.Inside).IsEqualTo(global::Avalonia.Media.Color.FromRgb(255, 0, 0));
        await Assert.That(colours.Outside).IsEqualTo(global::Avalonia.Media.Color.FromRgb(Back.Red, Back.Green, Back.Blue));
    }

    [Test]
    public async Task WithoutATitleBarTheHeadersBlankCellsAreTheWindowsNotTheClients()
    {
        var events = await Run(window =>
        {
            var (view, driver) = ((TerminalView, AvaloniaConsoleDriver))window.Tag!;
            driver.SetNarrowCell(2, 0, 'm', Color.Default, Color.Default);
            driver.Flush();
            var seen = new List<System.Drawing.Point>();
            driver.MouseEvent += (_, _, cell) => seen.Add(cell);
            var cell = view.SizeFor(1, 1);
            foreach (var column in new[] { 6, 2 })
            {
                var at = new Point((cell.Width * column) + (cell.Width / 2), cell.Height / 2 + 6);
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
            }

            return seen;
        }, WindowDecorations.BorderOnly);

        // The blank cell took hold of the window; the press on text reached the client, press and release.
        await Assert.That(events.All(c => c == new System.Drawing.Point(2, 0))).IsTrue();
        await Assert.That(events.Count).IsEqualTo(2);
    }

    [Test]
    public async Task CtrlAndShiftEnterReachTheDriverWithTheirModifier()
    {
        var keys = await Run(window =>
        {
            var (_, driver) = ((TerminalView, AvaloniaConsoleDriver))window.Tag!;
            var seen = new List<ConsoleKeyInfo>();
            driver.KeyPressed += (_, k) => seen.Add(k);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
            return seen;
        });

        await Assert.That(keys).IsEquivalentTo(new[]
        {
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, true),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, true, false, false),
        });
    }

    [Test]
    public async Task AltEnterMaximisesAndRestoresAndTheClientNeverSeesIt()
    {
        var (states, keys) = await Run(window =>
        {
            var (_, driver) = ((TerminalView, AvaloniaConsoleDriver))window.Tag!;
            var seen = new List<ConsoleKeyInfo>();
            driver.KeyPressed += (_, k) => seen.Add(k);
            var states = new List<WindowState>();
            foreach (var _ in new[] { 1, 2 })
            {
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Alt);
                states.Add(window.WindowState);
            }

            return (states, seen);
        });

        await Assert.That(states).IsEquivalentTo(new[] { WindowState.Maximized, WindowState.Normal });
        await Assert.That(keys).IsEmpty();
    }

    private static global::Avalonia.Media.Color Pixel(global::Avalonia.Media.Imaging.WriteableBitmap bitmap, double x, double y)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        var px = (int)x;
        var py = (int)y;
        var i = (py * buffer.RowBytes) + (px * 4);
        return buffer.Format == global::Avalonia.Platform.PixelFormat.Rgba8888
            ? global::Avalonia.Media.Color.FromRgb(bytes[i], bytes[i + 1], bytes[i + 2])
            : global::Avalonia.Media.Color.FromRgb(bytes[i + 2], bytes[i + 1], bytes[i]);
    }

    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

    private static Task<T> Run<T>(Func<Window, T> body, WindowDecorations decorations = WindowDecorations.Full) =>
        Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(10, 4);
            var view = new TerminalView(driver, SKColors.White, Back, 16, null);
            var window = new Window { Content = view, Tag = (view, driver), WindowDecorations = decorations };
            var size = view.SizeFor(10, 4);
            window.Width = size.Width;
            window.Height = size.Height;
            window.Show();
            view.Focus();
            try
            {
                return body(window);
            }
            finally
            {
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

    private static class Dispatch
    {
        public static void Drain() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}

public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new SimpleTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
