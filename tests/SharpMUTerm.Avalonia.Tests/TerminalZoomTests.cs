using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using SkiaSharp;

namespace SharpMUTerm.Avalonia.Tests;

public class TerminalZoomTests
{
    [Test]
    [Arguments(1.0)]
    [Arguments(1.25)]
    [Arguments(1.5)]
    public async Task ZoomResizesTheGridWithoutResizingTheWindowAndResetReusesFonts(double scale)
    {
        var result = await TerminalViewTests.Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(80, 24);
            var view = new TerminalView(driver, SKColors.White, SKColors.Black, 16, null);
            var size = view.SizeFor(80, 24);
            var window = new Window { Content = view, Width = size.Width, Height = size.Height };
            window.Show();
            view.PrepareScale(scale);
            try
            {
                var original = driver.ScreenSize;
                var renderer = view.Renderer;
                var resized = 0;
                driver.ScreenResized += (_, _) => resized++;
                view.SetZoom(150);
                var zoomed = driver.ScreenSize;
                var measured = driver.Pointer.CellHeightMeasured;
                var height = view.CellExtent.Height;
                view.SetZoom(100);
                return (original, zoomed, reset: driver.ScreenSize, sameFonts: ReferenceEquals(renderer, view.Renderer), resized, measured, height);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
        await Assert.That(result.zoomed.Width).IsLessThan(result.original.Width);
        await Assert.That(result.zoomed.Height).IsLessThan(result.original.Height);
        await Assert.That(result.reset.Width).IsEqualTo(result.original.Width);
        await Assert.That(result.reset.Height).IsEqualTo(result.original.Height);
        await Assert.That(result.sameFonts).IsTrue();
        await Assert.That(result.resized).IsEqualTo(2);
        await Assert.That(result.measured).IsEqualTo(result.height);
    }

    [Test]
    public async Task ZoomRepaintsEvenWhenTheMinimumGridAndPublishedFrameDoNotChange()
    {
        var result = await TerminalViewTests.Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(20, 8);
            var view = new TerminalView(driver, SKColors.White, SKColors.Black, 16, null);
            var window = new Window { Content = view, Width = 80, Height = 60 };
            window.Show();
            try
            {
                driver.FillCells(1, 0, 1, ' ', SharpConsoleUI.Color.Default, new SharpConsoleUI.Color(255, 0, 0));
                driver.Flush();
                DesktopLayerTests.Drain();
                byte[] Capture()
                {
                    using var bitmap = window.CaptureRenderedFrame()!;
                    using var pixels = bitmap.Lock();
                    var bytes = new byte[pixels.RowBytes * pixels.Size.Height];
                    System.Runtime.InteropServices.Marshal.Copy(pixels.Address, bytes, 0, bytes.Length);
                    return bytes;
                }

                var before = Capture();
                var frame = driver.Frame;
                view.SetZoom(110);
                DesktopLayerTests.Drain();
                return (sameFrame: ReferenceEquals(frame, driver.Frame), changedPixels: !before.SequenceEqual(Capture()));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
        await Assert.That(result.sameFrame).IsTrue();
        await Assert.That(result.changedPixels).IsTrue();
    }

    [Test]
    public async Task ZoomWorksWhileANativeEditorOwnsFocus()
    {
        var result = await TerminalViewTests.Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(80, 24);
            var view = new TerminalView(driver, SKColors.White, SKColors.Black, 16, null);
            var editor = new TextBox();
            var window = new Window { Content = editor, Width = 300, Height = 200 };
            view.AttachZoomKeys(window);
            window.Show();
            editor.Focus();
            try
            {
                window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.Control);
                window.KeyTextInput("=");
                var noZoomText = string.IsNullOrEmpty(editor.Text);
                window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.None);
                window.KeyTextInput("=");
                return (view.ZoomPercent, noZoomText, editor.Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
        await Assert.That(result.ZoomPercent).IsEqualTo(110);
        await Assert.That(result.noZoomText).IsTrue();
        await Assert.That(result.Text).IsEqualTo("=");
    }

    [Test]
    public async Task ZoomIsBoundedAndMenuActionsResetToLaunchSize()
    {
        var result = await TerminalViewTests.Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(80, 24);
            var view = new TerminalView(driver, SKColors.White, SKColors.Black, 20, null);
            view.SetZoom(999);
            var max = view.ZoomPercent;
            var disabledIn = !view.ZoomItems[0].Enabled;
            view.SetZoom(-1);
            var min = view.ZoomPercent;
            var disabledOut = !view.ZoomItems[1].Enabled;
            view.ZoomItems[2].Run();
            return (max, min, disabledIn, disabledOut, reset: view.ZoomPercent, text: view.CellTextSize);
        }, CancellationToken.None);
        await Assert.That(result.max).IsEqualTo(200);
        await Assert.That(result.min).IsEqualTo(50);
        await Assert.That(result.disabledIn && result.disabledOut).IsTrue();
        await Assert.That(result.reset).IsEqualTo(100);
        await Assert.That(result.text).IsEqualTo(20);
    }

    [Test]
    public async Task ZoomKeysAreLocalAndOrdinaryTypingStillReachesTheClient()
    {
        var result = await TerminalViewTests.Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(80, 24);
            var view = new TerminalView(driver, SKColors.White, SKColors.Black, 16, null);
            var sent = new List<ConsoleKeyInfo>();
            driver.KeyPressed += (_, key) => sent.Add(key);
            var zoom = new KeyEventArgs { Key = Key.OemPlus, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift };
            view.HandleKeyDown(zoom);
            var percent = view.ZoomPercent;
            view.HandleKeyDown(new KeyEventArgs { Key = Key.NumPad0, KeyModifiers = KeyModifiers.Control });
            view.HandleTextInput(new TextInputEventArgs { Text = "0" });
            view.HandleKeyDown(new KeyEventArgs { Key = Key.A, KeyModifiers = KeyModifiers.Control });
            return (percent, zoom.Handled, sent.Count, key: sent.Single().Key);
        }, CancellationToken.None);
        await Assert.That(result.percent).IsEqualTo(110);
        await Assert.That(result.Handled).IsTrue();
        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result.key).IsEqualTo(ConsoleKey.A);
        await Assert.That(TerminalView.ZoomFor(Key.OemPlus, KeyModifiers.None)).IsNull();
        await Assert.That(TerminalView.ZoomFor(Key.D0, KeyModifiers.Control | KeyModifiers.Alt)).IsNull();
        await Assert.That(TerminalView.ZoomFor(Key.Subtract, KeyModifiers.Control)).IsEqualTo(-10);
        await Assert.That(TerminalView.ZoomFor(Key.NumPad0, KeyModifiers.Control)).IsEqualTo(0);
    }
}
