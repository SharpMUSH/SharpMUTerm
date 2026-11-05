using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;
using SkiaSharp;
using CellColour = SharpConsoleUI.Color;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// Where the window's drawing meets the cells: a pane divider is drawn as the two panes' planes meeting at the tokens'
/// hairline (lit, the cells' own hover colour, wider); the typed text in the command line starts a mono space after the
/// UI-face prompt and a press there is a press on the cell drawn under it; and the bundled faces are made once, so a font
/// manager disposing its collection cannot take a face from under the next.
/// </summary>
public class WindowSeamTests
{
    private const int Columns = 40;
    private const int Rows = 10;

    private static readonly SKColor Back = new(0x10, 0x13, 0x1a);
    private static readonly CellColour Left = new(0x20, 0x30, 0x40);
    private static readonly CellColour Right = new(0x40, 0x30, 0x20);
    private static readonly CellColour RuleCells = new(0x80, 0x80, 0x80);
    private static readonly SKColor Hairline = new(0x2c, 0x34, 0x47);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ADividerIsThePanesMeetingAtAHairline(bool lit)
    {
        var (left, right, centre, edge) = await Run((window, view, driver) =>
        {
            driver.FillCells(0, 0, 19, ' ', CellColour.Default, Left);
            driver.FillCells(19, 0, 1, ' ', CellColour.Default, RuleCells);
            driver.FillCells(20, 0, 20, ' ', CellColour.Default, Right);
            ((IPointerPresenter)driver.Pointer).Dividers([new PaneDividerOverlay(new PaneRect(19, 0, 1, 1), Vertical: true, lit)]);
            driver.Flush();
            Drain();
            using var frame = window.CaptureRenderedFrame()!;
            var cw = (double)view.Renderer.Fonts.CellWidth;
            var ch = (double)view.Renderer.Fonts.CellHeight;
            var x = 19 * cw;
            var middle = Math.Round(x + ((cw - (lit ? 3 : 1)) / 2));
            return (Pixel(frame, x + 0.5, ch / 2), Pixel(frame, x + cw - 0.5, ch / 2), Pixel(frame, middle, ch / 2), Pixel(frame, middle + (lit ? 2 : 0), ch / 2));
        });

        await Assert.That(left).IsEqualTo(Color.FromRgb(Left.R, Left.G, Left.B)).Because("the left pane's plane runs up to the rule");
        await Assert.That(right).IsEqualTo(Color.FromRgb(Right.R, Right.G, Right.B)).Because("and the right pane's from it");
        var expected = lit ? Color.FromRgb(RuleCells.R, RuleCells.G, RuleCells.B) : Color.FromRgb(Hairline.Red, Hairline.Green, Hairline.Blue);
        await Assert.That(centre).IsEqualTo(expected).Because(lit ? "a lit divider keeps the cells' hover colour" : "the tokens' hairline");
        await Assert.That(edge).IsEqualTo(expected).Because(lit ? "three pixels wide, so it reads as a handle" : "one pixel");
    }

    [Test]
    public async Task TheTypedTextStartsAtTheFieldsPaddingAndAPressThereIsItsCell()
    {
        // A bar holding text has no placeholder, so nothing is drawn shifted: a press on a cell is that cell.
        var pressed = await Run((window, view, driver) =>
        {
            var band = new InputBand(Rows - 1, 1, InputLayout.HostFieldPad, [new InputPrompt(Rows - 1, "")], "");
            ((IPointerPresenter)driver.Pointer).InputBand(band);
            driver.Flush();
            Drain();
            using (window.CaptureRenderedFrame())
            {
            }

            var seen = new List<System.Drawing.Point>();
            driver.MouseEvent += (_, _, c) => seen.Add(c);
            var scale = view.Renderer.Fonts.CellWidth / view.SizeFor(1, 1).Width;
            var cw = view.Renderer.Fonts.CellWidth / scale;
            var ch = view.Renderer.Fonts.CellHeight / scale;
            var at = new Point((InputLayout.HostFieldPad * cw) + (cw / 2), ((Rows - 1) * ch) + NativeLayout.FieldTop + (ch / 2));
            window.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
            window.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
            return seen.FirstOrDefault();
        }, field: true);

        await Assert.That(pressed).IsEqualTo(new System.Drawing.Point(InputLayout.HostFieldPad, Rows - 1))
            .Because("the text's first cell is the first inside the field's padding");
    }

    [Test]
    public async Task TheBundledFacesAreMadeOnceAndOutliveAManagerDisposingThem()
    {
        var (first, second, glyph) = await TerminalViewTests.Session.Dispatch(() =>
        {
            var a = new BundledFonts();
            a.TryGetGlyphTypeface("Inter", FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, out var one);
            ((IDisposable)a).Dispose();
            var b = new BundledFonts();
            b.TryGetGlyphTypeface("Inter", FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, out var two);
            two!.CharacterToGlyphMap.TryGetGlyph('A', out var index);
            return (one, two, index);
        }, CancellationToken.None);

        await Assert.That(ReferenceEquals(first, second)).IsTrue().Because("one face per process, shared by every collection");
        await Assert.That((int)glyph).IsGreaterThan(0).Because("and still usable after a collection holding it was disposed");
    }

    private static void Drain() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static Task<T> Run<T>(Func<Window, TerminalView, AvaloniaConsoleDriver, T> body, bool field = false) =>
        TerminalViewTests.Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(Columns, Rows);
            var view = new TerminalView(driver, SKColors.White, Back, 16, null) { DividerRule = Hairline, DrawsInputField = field };
            var window = new Window { Content = view };
            var size = view.SizeFor(Columns, Rows);
            window.Width = size.Width;
            window.Height = size.Height;
            window.Show();
            try
            {
                return body(window, view, driver);
            }
            finally
            {
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

    private static Color Pixel(global::Avalonia.Media.Imaging.WriteableBitmap bitmap, double x, double y)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        var i = ((int)y * buffer.RowBytes) + ((int)x * 4);
        return buffer.Format == global::Avalonia.Platform.PixelFormat.Rgba8888
            ? Color.FromRgb(bytes[i], bytes[i + 1], bytes[i + 2])
            : Color.FromRgb(bytes[i + 2], bytes[i + 1], bytes[i]);
    }
}
