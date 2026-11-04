using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Tui;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window's native modals (<see cref="ModalPresenter"/>), driven from the real client in the window host: each opens
/// as an overlay over everything with its first control focused and no cell modal under it; the quit prompt stays the
/// last child of the root whatever is added after it, F1 included; keys mean what they mean in cells; Esc gives the
/// keyboard back; and the card follows the client's dark or light theme.
/// </summary>
[NotInParallel]
public class NativeModalTests
{
    private sealed record Rig(Window Window, Panel Root, TerminalView View, SharpMUTermApp App, AvaloniaConsoleDriver Driver);

    private static readonly ConsoleKeyInfo CtrlQ = new('\x11', ConsoleKey.Q, false, false, true);
    private static readonly ConsoleKeyInfo CtrlF = new('\x06', ConsoleKey.F, false, false, true);

    private static Task<T> Run<T>(Func<Rig, T> body, string theme = "Dark") =>
        TerminalViewTests.Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(120, 34);
            var view = new TerminalView(driver, SKColors.White, new SKColor(0x1e, 0x1e, 0x24), 14, null);
            var root = new Panel();
            root.Children.Add(view);
            var layer = new DesktopLayer(view, HostChrome.HeaderAndRail);
            root.Children.Add(layer);
            driver.Composer.Attach(root, layer, view, () => view.Focus());
            driver.Palette.Attach(root, () => view.Focus());
            var window = new Window { Content = root, Width = 1000, Height = 700 };
            driver.Modals.Attach(window, root, () => view.Focus(), driver.RaiseKey);
            window.Show();
            view.Focus();
            var config = DemoScene.Build();
            config.ThemeName = theme;
            config.Theme = ThemeLibrary.Get(theme);
            var app = new SharpMUTermApp(config, WindowHost.Create(driver, clipboard: null) with
            {
                Audio = null,
                Presence = null,
                Announcer = null,
                NativeSettings = null,
                RunsInline = true,
            });
            driver.KeyPressed += (_, key) => app.SimulateKey(key);
            try
            {
                DesktopLayerTests.Drain();
                return body(new Rig(window, root, view, app, driver));
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

    private static void Press(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = target });
        DesktopLayerTests.Drain();
    }

    private static void Key(Rig rig, ConsoleKeyInfo key)
    {
        rig.App.SimulateKey(key);
        DesktopLayerTests.Drain();
    }

    [Test]
    public async Task CtrlFOpensTheNativeSearchOverEverythingWithTheBoxFocused()
    {
        var (focused, cellOpen, onTop, colours) = await Run(rig =>
        {
            Key(rig, CtrlF);
            var overlay = rig.Driver.Modals.Modal!;
            var view = (SearchView)overlay.View;
            return (view.Box.IsFocused, rig.App.DrawnModel(Client.Surfaces.SurfaceCatalog.Search) is not null,
                ReferenceEquals(rig.Root.Children[^1], overlay), overlay.Colours);
        });

        await Assert.That(focused).IsTrue();
        await Assert.That(cellOpen).IsFalse().Because("no cell search under the native one");
        await Assert.That(onTop).IsTrue().Because("nothing native draws over it");
        await Assert.That(colours).IsEqualTo(NativeTokens.Dark).Because("the demo's theme is Dark");
    }

    /// <summary>
    /// The <c>history-search-filter</c> view opens the native ⌃R with its query in the box, so the window's frame lists
    /// the filtered rows the cells list — not the whole history.
    /// </summary>
    [Test]
    public async Task TheHistoryFilterViewOpensTheNativeListFiltered()
    {
        var (text, rows, all) = await Run(rig =>
        {
            rig.App.RenderSnapshot("history-search-filter");
            DesktopLayerTests.Drain();
            var view = (HistoryView)rig.Driver.Modals.Modal!.View;
            return (view.Box.Text, view.Matches.Select(m => m.Text).ToArray(), ((HistorySession)rig.App.NativeHistory!).Model.Entries.Count);
        });

        await Assert.That(text).IsEqualTo("north");
        await Assert.That(rows).IsNotEmpty();
        await Assert.That(rows.Length).IsLessThan(all).Because("the list is filtered, not the whole history");
        await Assert.That(rows.All(r => r.Contains("north", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task TheCardFollowsALightTheme()
    {
        var colours = await Run(rig =>
        {
            Key(rig, CtrlQ);
            return rig.Driver.Modals.Quit!.Colours;
        }, theme: "Light");

        await Assert.That(colours).IsEqualTo(NativeTokens.Light);
    }

    [Test]
    public async Task TypingSearchesThroughTheClientAndEnterLandsAndGivesTheKeyboardBack()
    {
        var (rows, expected, bar, closed, viewFocused) = await Run(rig =>
        {
            rig.App.RenderSnapshot(null);
            rig.View.Focus();
            Key(rig, CtrlF);
            var view = (SearchView)rig.Driver.Modals.Modal!.View;
            view.Box.Text = "the";
            DesktopLayerTests.Drain();
            var drawn = view.Model.Rows.ToArray();
            var model = rig.App.SearchModelFor("the", false, false).Rows.ToArray();
            Press(view.Box, global::Avalonia.Input.Key.Enter);
            return (drawn, model, rig.App.SearchBarIndex(rig.App.ActiveWindowId()), rig.Driver.Modals.Modal is null, rig.View.IsFocused);
        });

        await Assert.That(rows).IsNotEmpty();
        await Assert.That(rows).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(bar).IsNotNull().Because("⏎ went to the hit through the client's path");
        await Assert.That(closed).IsTrue();
        await Assert.That(viewFocused).IsTrue().Because("the keyboard goes back to the cells, and so to the command line");
    }

    [Test]
    public async Task AltEAndAltATogglesAreTheCellSurfacesKeys()
    {
        var (regex, all) = await Run(rig =>
        {
            Key(rig, CtrlF);
            var view = (SearchView)rig.Driver.Modals.Modal!.View;
            Press(view.Box, global::Avalonia.Input.Key.E, KeyModifiers.Alt);
            Press(view.Box, global::Avalonia.Input.Key.A, KeyModifiers.Alt);
            return (view.Model.Regex, view.Model.AllWindows);
        });

        await Assert.That(regex).IsTrue();
        await Assert.That(all).IsTrue();
    }

    [Test]
    public async Task EscClosesAndGivesTheKeyboardBack()
    {
        var (open, focused) = await Run(rig =>
        {
            Key(rig, CtrlF);
            Press(((SearchView)rig.Driver.Modals.Modal!.View).Box, global::Avalonia.Input.Key.Escape);
            return (rig.App.SearchIsOpen, rig.View.IsFocused);
        });

        await Assert.That(open).IsFalse();
        await Assert.That(focused).IsTrue();
    }

    [Test]
    public async Task TheQuitPromptStaysInFrontOfAnythingAddedAfterIt()
    {
        var (afterSearch, afterAdd, searchUnder) = await Run(rig =>
        {
            Key(rig, CtrlF);
            Key(rig, CtrlQ);
            var quit = rig.Driver.Modals.Quit!;
            var first = ReferenceEquals(rig.Root.Children[^1], quit);

            // Something native added later (a toast, ⌃P, F1) goes under the prompt.
            rig.Root.Children.Add(new Border());
            DesktopLayerTests.Drain();
            var second = ReferenceEquals(rig.Root.Children[^1], quit);
            var under = rig.Root.Children.IndexOf(rig.Driver.Modals.Modal!) < rig.Root.Children.IndexOf(quit);
            return (first, second, under);
        });

        await Assert.That(afterSearch).IsTrue();
        await Assert.That(afterAdd).IsTrue().Because("the prompt is moved back to the end");
        await Assert.That(searchUnder).IsTrue();
    }

    /// <summary>
    /// The known bug: the window's close button (⌃Q) while F1 is open left the cell prompt under the composer. Now the
    /// prompt is native and in front, F1 stays open under it, and Stay (N) goes back to the post.
    /// </summary>
    [Test]
    public async Task CtrlQOverTheNativeComposerPutsThePromptInFrontAndStayReturnsToThePost()
    {
        var (composerOpen, quitOnTop, quitFocused, afterStay, cellPrompt) = await Run(rig =>
        {
            Key(rig, new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false));
            var composer = rig.Driver.Composer.Overlay;
            Key(rig, CtrlQ);
            var quit = rig.Driver.Modals.Quit!;
            var onTop = ReferenceEquals(rig.Root.Children[^1], quit);
            var view = (QuitView)quit.View;
            var focused = view.StayButton.IsFocused;
            var cell = rig.App.DrawnModel(Client.Surfaces.SurfaceCatalog.Quit) is not null;
            Press(view.StayButton, global::Avalonia.Input.Key.N);
            return (composer is not null && rig.Root.Children.Contains(composer), onTop, focused, rig.App.NativeComposerIsOpen, cell);
        });

        await Assert.That(composerOpen).IsTrue();
        await Assert.That(quitOnTop).IsTrue();
        await Assert.That(quitFocused).IsTrue().Because("Stay is chosen and has the keyboard");
        await Assert.That(afterStay).IsTrue().Because("Stay goes back to F1");
        await Assert.That(cellPrompt).IsFalse().Because("no cell prompt is drawn under the native composer");
    }

    [Test]
    public async Task YInTheNativePromptQuitsAndTabMovesTheChoice()
    {
        var (afterTab, exited) = await Run(rig =>
        {
            Key(rig, CtrlQ);
            var view = (QuitView)rig.Driver.Modals.Quit!.View;
            Press(view.StayButton, global::Avalonia.Input.Key.Tab);
            var selected = view.Selected;
            Press(view.QuitButton, global::Avalonia.Input.Key.Y);
            return (selected, rig.App.ExitRequested);
        });

        await Assert.That(afterTab).IsEqualTo(QuitChoice.Quit);
        await Assert.That(exited).IsTrue();
    }

    /// <summary>
    /// The import review in the window: the card shows what can act as the player, the check box stores the file's
    /// password through the client, and Import writes the world, character and set — nothing before it.
    /// </summary>
    [Test]
    public async Task TheImportReviewWritesNothingUntilImport_AndItsCheckBoxStoresThePassword()
    {
        var (body, boxShown, worldsBefore, stored, worlds, focused) = await Run(rig =>
        {
            rig.App.RenderSnapshot("import-review");
            DesktopLayerTests.Drain();
            var view = (ImportReviewView)rig.Driver.Modals.Modal!.View;
            var before = rig.App.Configuration.Worlds.Count;
            var body = view.BodyText.ToArray();
            var focused = view.ImportButton.IsFocused;
            view.PasswordsBox.IsChecked = true;
            DesktopLayerTests.Drain();
            var stored = rig.App.PendingImport!.StorePasswords;
            view.ImportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DesktopLayerTests.Drain();
            var wren = rig.App.Configuration.Worlds.SelectMany(w => w.Characters).Single(c => c.Name == "Wren");
            return (body, view.PasswordsBox.IsVisible, before, stored && wren.Password == "not-a-real-one", rig.App.Configuration.Worlds.Count - before, focused);
        });

        await Assert.That(body).Contains(t => t.StartsWith("WHAT CAN ACT AS YOU", StringComparison.OrdinalIgnoreCase));
        await Assert.That(body).Contains(t => t.Contains("Northwind", StringComparison.Ordinal));
        await Assert.That(boxShown).IsTrue();
        await Assert.That(focused).IsTrue();
        await Assert.That(stored).IsTrue();
        await Assert.That(worlds).IsEqualTo(1);
        await Assert.That(worldsBefore).IsGreaterThan(0).Because("the demo has worlds of its own, which the import leaves alone");
    }
}
