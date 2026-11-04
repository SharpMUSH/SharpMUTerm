using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Tui;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window as a person runs it (<see cref="HostChrome.Desktop"/>): the bundled faces resolve, the status footer draws
/// what the client said and hands back only the targets the client put on it, and the field frames the bars' rows.
/// </summary>
public class NativeFooterTests
{
    private const int Columns = 100;
    private const int Rows = 24;
    private static readonly SKColor Back = new(0x1e, 0x1e, 0x25);

    [Test]
    public async Task TheBundledFacesResolveByNameOnEveryPlatform()
    {
        var (loaded, ui, mono, plain) = await Session.Dispatch(() =>
        {
            var manager = FontManager.Current;
            manager.TryGetGlyphTypeface(new Typeface(NativeTokens.UiFont), out var uiFace);
            manager.TryGetGlyphTypeface(new Typeface(NativeTokens.MonoFont), out var monoFace);
            manager.TryGetGlyphTypeface(new Typeface("Inter"), out var plainFace);
            return (new BundledFonts().Loaded, uiFace?.FamilyName, monoFace?.FamilyName, plainFace?.FamilyName);
        }, CancellationToken.None);

        await Assert.That(loaded).IsEqualTo(8);
        await Assert.That(ui).IsEqualTo("Inter");
        await Assert.That(mono).IsEqualTo("JetBrains Mono");
        await Assert.That(plain).IsEqualTo("Inter").Because("a view naming Inter gets the bundle, not an installed face");
    }

    [Test]
    public async Task TheFooterDrawsWhatTheClientSaidAndHandsBackOnlyItsTargets()
    {
        var activated = new List<string>();
        var model = StatusModel.From(
            StatusModel.Mark([new("Corvid · connected", "#cccccc", false), new("      ", "#cccccc", false), new("⌃P palette", "#cccccc", false)], "⌃P palette", HeaderModel.PaletteTarget),
            "#101014");

        var (visible, top, height, hits, text, clicked) = await Run((window, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Status(new StatusOverlay(model, true, activated.Add));
            Drain();
            window.CaptureRenderedFrame()?.Dispose();
            var footer = layer.Footer;
            var bounds = new Rect(footer.TranslatePoint(default, window)!.Value, footer.Bounds.Size);
            var hit = footer.Hits.Single();
            var centre = footer.TranslatePoint(hit.Area.Center, window)!.Value;
            window.MouseMove(centre);
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            Drain();
            footer.Click("term:quit");
            return (footer.IsVisible, bounds.Bottom, bounds.Height, footer.Hits.Select(h => h.Target).ToList(), footer.Text, activated.ToList());
        });

        await Assert.That(visible).IsTrue();
        await Assert.That(height).IsEqualTo(NativeLayout.StatusHeight);
        await Assert.That(top).IsGreaterThan(0);
        await Assert.That(hits).IsEquivalentTo(new[] { HeaderModel.PaletteTarget });
        await Assert.That(text).Contains("Ctrl+P palette").Because("keys in words");

        // The click hands back the palette; a target the client never offered still goes to the client, which refuses it.
        await Assert.That(clicked.First()).IsEqualTo(HeaderModel.PaletteTarget);
    }

    [Test]
    public async Task TheFieldIsInsetFromTheGridAndFramesTheBarsRows()
    {
        var band = new InputBand(20, 2, InputLayout.HostFieldPad, [new InputPrompt(20, "Corvid ›")], "⏎ sends");
        var rect = TerminalView.FieldRect(band, 10, 20, 80, 1f);

        await Assert.That((double)rect.Left).IsEqualTo((3 * 10) - NativeLayout.FieldInsetX);
        await Assert.That((double)rect.Right).IsEqualTo(800 - ((3 * 10) - NativeLayout.FieldInsetX));
        await Assert.That((double)rect.Top).IsEqualTo((20 * 20) + NativeLayout.FieldOuter);
        await Assert.That((double)rect.Bottom).IsEqualTo((22 * 20) + NativeLayout.FieldTop + NativeLayout.FieldInner);

        // The grid gives up exactly the field's padding: the rows it frames are still whole cells.
        var sizes = await Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(Columns, Rows);
            var view = new TerminalView(driver, SKColors.White, Back, 16, null) { DrawsInputField = true };
            var plain = new TerminalView(driver, SKColors.White, Back, 16, null);
            return (view.SizeFor(Columns, Rows).Height, plain.SizeFor(Columns, Rows).Height);
        }, CancellationToken.None);
        await Assert.That(sizes.Item1 - sizes.Item2).IsEqualTo(NativeLayout.FieldReserve);
    }

    private static void Drain() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static Task<T> Run<T>(Func<Window, DesktopLayer, AvaloniaConsoleDriver, T> body) =>
        Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(Columns, Rows);
            var view = new TerminalView(driver, SKColors.White, Back, 16, null);
            var layer = new DesktopLayer(view, HostChrome.Desktop);
            var root = new Panel();
            root.Children.Add(view);
            root.Children.Add(layer);
            var window = new Window { Content = root, WindowDecorations = WindowDecorations.BorderOnly };
            var cells = view.SizeFor(Columns, Rows);
            window.Width = cells.Width + NativeLayout.SidebarWidth + NativeLayout.SidebarGutter;
            window.Height = cells.Height + NativeLayout.HeaderHeight + NativeLayout.StatusHeight;
            window.Show();
            view.Focus();
            driver.Flush();
            Drain();
            try
            {
                return body(window, layer, driver);
            }
            finally
            {
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

    private static HeadlessUnitTestSession Session => TerminalViewTests.Session;
}
