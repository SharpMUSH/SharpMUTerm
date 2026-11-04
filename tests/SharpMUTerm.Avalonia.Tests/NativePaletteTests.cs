using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Tui;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window's native ⌃P (<see cref="PaletteOverlay"/>), driven from the real client in the window host: it lists the
/// shared model's rows for the query in its box, ⏎ and a click run the selected id through the cell palette's own
/// path, Esc closes and gives the keyboard back to the cells, and the key column is in the window's keys.
/// </summary>
[NotInParallel]
public class NativePaletteTests
{
    private sealed record Rig(Window Window, TerminalView View, SharpMUTermApp App, AvaloniaConsoleDriver Driver);

    private static readonly ConsoleKeyInfo CtrlP = new('\x10', ConsoleKey.P, false, false, true);

    private static Task<T> Run<T>(Func<Rig, T> body) =>
        Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(120, 34);
            var view = new TerminalView(driver, SKColors.White, new SKColor(0x1e, 0x1e, 0x24), 14, null);
            var root = new Panel();
            root.Children.Add(view);
            root.Children.Add(new DesktopLayer(view, HostChrome.HeaderAndRail));
            driver.Palette.Attach(root, () => view.Focus());
            var window = new Window { Content = root, Width = 1000, Height = 700 };
            window.Show();
            view.Focus();
            var app = new SharpMUTermApp(DemoScene.Build(), WindowHost.Create(driver, clipboard: null) with
            {
                Audio = null,
                Presence = null,
                Announcer = null,
                NativeSettings = null,
                RunsInline = true,
            });
            try
            {
                DesktopLayerTests.Drain();
                return body(new Rig(window, view, app, driver));
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

    private static PaletteOverlay Open(Rig rig)
    {
        rig.App.SimulateKey(CtrlP);
        DesktopLayerTests.Drain();
        return rig.Driver.Palette.Overlay ?? throw new InvalidOperationException("the palette did not open");
    }

    private static void Type(PaletteOverlay overlay, string query)
    {
        overlay.SearchBox.Text = query;
        DesktopLayerTests.Drain();
    }

    private static void Press(PaletteOverlay overlay, Key key)
    {
        overlay.SearchBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = overlay.SearchBox });
        DesktopLayerTests.Drain();
    }

    private static string[] ModelRows(PaletteView view) =>
        view.Rows.Select(r => r is PaletteHeader h ? h.Label : ((PaletteEntry)r).Item.Title).ToArray();

    [Test]
    public async Task CtrlPOpensTheNativePaletteWithTheSearchFocusedAndNoCellPopup()
    {
        var (focused, cellOpen, onTop) = await Run(rig =>
        {
            var overlay = Open(rig);
            var root = (Panel)rig.Window.Content!;
            return (overlay.SearchBox.IsFocused, rig.App.Palette.IsOpen, ReferenceEquals(root.Children[^1], overlay));
        });

        await Assert.That(focused).IsTrue();
        await Assert.That(cellOpen).IsFalse();
        await Assert.That(onTop).IsTrue().Because("nothing native draws over it: it is the root panel's last child");
    }

    [Test]
    public async Task ThePaletteCentresOverTheWholeWindowAndClearsTheNativeHeader()
    {
        var (centre, windowCentre, top, width) = await Run(rig =>
        {
            var overlay = Open(rig);
            rig.Window.UpdateLayout();
            DesktopLayerTests.Drain();
            var card = new Rect(overlay.Card.TranslatePoint(default, rig.Window)!.Value, overlay.Card.Bounds.Size);
            return (card.Center.X, rig.Window.ClientSize.Width / 2, card.Top, card.Width);
        });

        // The window, not the cells: the sidebar takes the left of it, and a card centred on the cells would sit off-centre.
        await Assert.That(Math.Abs(centre - windowCentre)).IsLessThan(1.0);
        await Assert.That(top).IsGreaterThanOrEqualTo(NativeLayout.HeaderHeight).Because("the search box opens under the header, not over it");
        await Assert.That(width).IsGreaterThan(0);
    }

    [Test]
    public async Task TheRowsAreTheModelsForTheSameQuery()
    {
        var results = await Run(rig =>
        {
            rig.App.RunFromPalette("layout:split-right");
            var overlay = Open(rig);
            var list = new List<(string Query, string[] Drawn, string[] Model, string Status, string Count)>();
            foreach (var query in new[] { string.Empty, "split", "frz", "zzzz" })
            {
                Type(overlay, query);
                var model = CommandPaletteModel.Build(query, rig.App.BuildCatalog(), rig.App.RecentCommandIds, rig.App.SuggestedCommandIds);
                list.Add((query, overlay.RowTexts.ToArray(), ModelRows(model), overlay.StatusText, $"{model.Entries.Count} of {model.Total}"));
            }

            return list;
        });

        foreach (var (query, drawn, model, status, count) in results)
        {
            await Assert.That(drawn).IsEquivalentTo(model, TUnit.Assertions.Enums.CollectionOrdering.Matching).Because($"query '{query}'");
            await Assert.That(status).StartsWith(count);
        }

        await Assert.That(results[0].Drawn[0]).IsEqualTo(CommandPaletteModel.RecentLabel);
    }

    [Test]
    public async Task MatchedCharactersAreHighlighted()
    {
        var bold = await Run(rig =>
        {
            var overlay = Open(rig);
            Type(overlay, "split");
            var title = overlay.EntryRows[0].GetVisualDescendantsOf<TextBlock>().First();
            return title.Inlines!.OfType<Run>().Where(r => r.FontWeight == global::Avalonia.Media.FontWeight.Bold).Select(r => r.Text).ToArray();
        });

        await Assert.That(string.Concat(bold)).IsEqualTo("Split").Because("the matched run is drawn bold in the accent");
    }

    [Test]
    public async Task EnterRunsTheSelectedIdThroughThePalettesOwnPath()
    {
        var (expected, recent, panes, open, focused) = await Run(rig =>
        {
            var overlay = Open(rig);
            Type(overlay, "split right");
            Press(overlay, Key.Down);
            Press(overlay, Key.Up);
            var id = overlay.View.Entries[overlay.Selected].Item.Id;
            Press(overlay, Key.Enter);
            return (id, rig.App.RecentCommandIds.FirstOrDefault(), rig.App.PaneOutputRects().Count, rig.Driver.Palette.Overlay is not null, rig.View.IsFocused);
        });

        await Assert.That(expected).IsEqualTo("layout:split-right");
        await Assert.That(recent).IsEqualTo(expected);
        await Assert.That(panes).IsEqualTo(2);
        await Assert.That(open).IsFalse();
        await Assert.That(focused).IsTrue().Because("the keyboard goes back to the cells, and so to the command line");
    }

    [Test]
    public async Task EscapeClosesRunsNothingAndGivesTheKeyboardBack()
    {
        var (open, clientOpen, recent, focused) = await Run(rig =>
        {
            var overlay = Open(rig);
            Type(overlay, "split");
            Press(overlay, Key.Escape);
            return (rig.Driver.Palette.Overlay is not null, rig.App.NativePaletteIsOpen, rig.App.RecentCommandIds.Count, rig.View.IsFocused);
        });

        await Assert.That(open).IsFalse();
        await Assert.That(clientOpen).IsFalse();
        await Assert.That(recent).IsEqualTo(0);
        await Assert.That(focused).IsTrue();
    }

    [Test]
    public async Task HoverSelectsAndAClickRuns()
    {
        var (hovered, ran, expected) = await Run(rig =>
        {
            var overlay = Open(rig);
            Type(overlay, "split");
            var row = overlay.EntryRows[1];
            var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), rig.Window)!.Value;
            rig.Window.MouseMove(centre);
            DesktopLayerTests.Drain();
            var selected = overlay.Selected;
            var id = overlay.View.Entries[1].Item.Id;
            rig.Window.MouseDown(centre, MouseButton.Left);
            rig.Window.MouseUp(centre, MouseButton.Left);
            DesktopLayerTests.Drain();
            return (selected, rig.App.RecentCommandIds.FirstOrDefault(), id);
        });

        await Assert.That(hovered).IsEqualTo(1);
        await Assert.That(ran).IsEqualTo(expected);
    }

    [Test]
    public async Task TheKeyColumnIsInTheWindowsKeys()
    {
        var (newline, any) = await Run(rig =>
        {
            var overlay = Open(rig);
            var index = overlay.View.Entries.ToList().FindIndex(e => e.Item.Id == "term:newline");
            return (index < 0 ? null : overlay.KeyTexts[index], overlay.KeyTexts.Where(k => k is not null).ToArray());
        });

        await Assert.That(newline).IsEqualTo("Ctrl+⏎");
        await Assert.That(any).DoesNotContain("Alt+⏎");
        await Assert.That(any.Any(k => k!.Contains('⌃') || k.Contains('⌥'))).IsFalse().Because("the window's UI font reads keys in words");
    }

    private static HeadlessUnitTestSession Session => TerminalViewTests.Session;
}
