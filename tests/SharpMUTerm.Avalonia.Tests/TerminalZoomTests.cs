using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using SkiaSharp;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

public class TerminalZoomTests
{
    [Test]
    public async Task ReservedZoomChordsMatchTheirActions()
    {
        await Assert.That(WindowHost.Zoom.Count).IsEqualTo(7);
        foreach (var reserved in WindowHost.Zoom)
        {
            var key = Enum.Parse<Key>(reserved.Chord.Key.ToString());
            var modifiers = KeyModifiers.Control;
            if (reserved.Chord.Modifiers.HasFlag(ConsoleModifiers.Shift)) modifiers |= KeyModifiers.Shift;
            var expected = reserved.Action switch { "zooms in" => 1, "zooms out" => -1, "resets terminal zoom" => 0, _ => int.MinValue };
            await Assert.That(TerminalView.ZoomFor(key, modifiers)).IsEqualTo(expected);
        }
    }

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
                var imageCells = ((IPointerPresenter)driver.Pointer).ImageCellSize;
                var expectedImageCells = new ImageCellSize(view.CellExtent.Width, view.CellExtent.Height);
                view.SetZoom(100);
                return (original, zoomed, reset: driver.ScreenSize, sameFonts: ReferenceEquals(renderer, view.Renderer), resized, measured, height, imageCells, expectedImageCells,
                    resetImageCells: ((IPointerPresenter)driver.Pointer).ImageCellSize,
                    expectedResetImageCells: new ImageCellSize(view.CellExtent.Width, view.CellExtent.Height));
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
        await Assert.That(result.imageCells).IsEqualTo(result.expectedImageCells);
        await Assert.That(result.resetImageCells).IsEqualTo(result.expectedResetImageCells);
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
    public async Task ZoomStepsThroughTheLevelsIsBoundedAndTheMenuResets()
    {
        var result = await TerminalViewTests.Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(80, 24);
            var view = new TerminalView(driver, SKColors.White, SKColors.Black, 20, null);
            var seen = new List<int>();
            for (var i = 0; i < 20; i++)
            {
                view.RequestZoom(1);
                seen.Add(view.ZoomPercent);
            }

            var max = view.ZoomPercent;
            var disabledIn = !view.ZoomItems[0].Enabled;
            for (var i = 0; i < 20; i++)
            {
                view.RequestZoom(-1);
            }

            var min = view.ZoomPercent;
            var disabledOut = !view.ZoomItems[1].Enabled;
            view.ZoomItems[2].Run();
            return (seen, max, min, disabledIn, disabledOut, reset: view.ZoomPercent, text: view.CellTextSize);
        }, CancellationToken.None);
        await Assert.That(result.seen.Take(4)).IsEquivalentTo(new[] { 110, 125, 150, 175 });
        await Assert.That(result.max).IsEqualTo(300);
        await Assert.That(result.min).IsEqualTo(50);
        await Assert.That(result.disabledIn && result.disabledOut).IsTrue();
        await Assert.That(result.reset).IsEqualTo(100);
        await Assert.That(result.text).IsEqualTo(20);
    }

    [Test]
    public async Task WithAListenerTheViewAsksAndDoesNotZoomItself()
    {
        var result = await TerminalViewTests.Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(80, 24);
            var view = new TerminalView(driver, SKColors.White, SKColors.Black, 16, null);
            var asked = new List<int>();
            view.ZoomRequested += asked.Add;
            view.ZoomShown = () => 125;
            view.HandleKeyDown(new KeyEventArgs { Key = Key.OemMinus, KeyModifiers = KeyModifiers.Control });
            return (asked, view.ZoomPercent, menu: view.ZoomItems.Select(i => i.Label).ToArray());
        }, CancellationToken.None);
        await Assert.That(result.asked).IsEquivalentTo(new[] { -1 });
        await Assert.That(result.ZoomPercent).IsEqualTo(100);
        await Assert.That(result.menu[0]).IsEqualTo("Zoom in (150%)");
        await Assert.That(result.menu[2]).IsEqualTo("Reset zoom (125%)");
    }

    [Test]
    public async Task CtrlAndTheWheelZoomANotchAtATime()
    {
        var remainder = 0.0;
        await Assert.That(TerminalView.ZoomForWheel(ref remainder, KeyModifiers.Control, 1)).IsEqualTo(1);
        await Assert.That(TerminalView.ZoomForWheel(ref remainder, KeyModifiers.Control, -1)).IsEqualTo(-1);
        await Assert.That(TerminalView.ZoomForWheel(ref remainder, KeyModifiers.None, 1)).IsNull();
        await Assert.That(TerminalView.ZoomForWheel(ref remainder, KeyModifiers.Control | KeyModifiers.Shift, 1)).IsNull();

        // A touchpad's small deltas add up to a notch before they zoom.
        remainder = 0;
        await Assert.That(TerminalView.ZoomForWheel(ref remainder, KeyModifiers.Control, 0.4)).IsEqualTo(0);
        await Assert.That(TerminalView.ZoomForWheel(ref remainder, KeyModifiers.Control, 0.7)).IsEqualTo(1);
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
        await Assert.That(TerminalView.ZoomFor(Key.Subtract, KeyModifiers.Control)).IsEqualTo(-1);
        await Assert.That(TerminalView.ZoomFor(Key.NumPad0, KeyModifiers.Control)).IsEqualTo(0);
    }
}
