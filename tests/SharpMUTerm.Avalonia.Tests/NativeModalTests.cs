using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

    /// <summary>
    /// Every native modal lies under one veil, the palette's and the settings window's (<see cref="NativeTokens.Scrim"/>):
    /// the whole window is dimmed, so the cells behind do not look live. Two stacked modals are two veils, the quit
    /// prompt's in front.
    /// </summary>
    [Test]
    public async Task EveryModalIsVeiledByTheOneScrimOverTheWholeWindow()
    {
        var (searchVeil, quitVeil, spans, inFront) = await Run(rig =>
        {
            Key(rig, CtrlF);
            var search = rig.Driver.Modals.Modal!;
            Key(rig, CtrlQ);
            var quit = rig.Driver.Modals.Quit!;
            return ((search.Background as SolidColorBrush)?.Color, (quit.Background as SolidColorBrush)?.Color,
                search.Bounds.Width >= rig.Root.Bounds.Width - 1 && search.Bounds.Height >= rig.Root.Bounds.Height - 1,
                ReferenceEquals(rig.Root.Children[^1], quit));
        });

        await Assert.That(searchVeil).IsEqualTo(NativeTokens.Dark.Scrim);
        await Assert.That(quitVeil).IsEqualTo(NativeTokens.Dark.Scrim);
        await Assert.That(spans).IsTrue().Because("the veil covers the header, the sidebar and the cells");
        await Assert.That(inFront).IsTrue();
        await Assert.That(NativeTokens.Dark.Scrim.A).IsGreaterThanOrEqualTo((byte)0x70).Because("faint enough to read through, dark enough to read as a veil");
        await Assert.That(NativeTokens.Light.Scrim.A).IsGreaterThanOrEqualTo((byte)0x48);
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

    /// <summary>
    /// The welcome is the window's first page, the mockup's: the ground plane over the whole window rather than a card on
    /// the scrim, and a press on that ground leaves it open (there is nothing outside it to dismiss it into).
    /// </summary>
    [Test]
    public async Task TheWelcomeIsAPageOnTheGroundOverTheWholeWindow_AndAPressOnItsGroundKeepsItOpen()
    {
        var (fullPage, plane, spans, open) = await Run(rig =>
        {
            rig.App.RenderSnapshot("welcome");
            DesktopLayerTests.Drain();
            var modal = rig.Driver.Modals.Modal!;
            var spans = modal.Card.Bounds.Width >= rig.Root.Bounds.Width - 1 && modal.Card.Bounds.Height >= rig.Root.Bounds.Height - 1;
            rig.Window.MouseDown(new Point(12, rig.Root.Bounds.Height / 2), MouseButton.Left);
            rig.Window.MouseUp(new Point(12, rig.Root.Bounds.Height / 2), MouseButton.Left);
            DesktopLayerTests.Drain();
            return (((IModalView)modal.View).FullPage, (modal.Background as SolidColorBrush)?.Color, spans, !modal.Session.IsEnded);
        });

        await Assert.That(fullPage).IsTrue();
        await Assert.That(plane).IsEqualTo(NativeTokens.Dark.Well).Because("the page sits on the ground, the mockup's #10131a");
        await Assert.That(spans).IsTrue();
        await Assert.That(open).IsTrue();
    }

    private static WelcomeView WelcomePage(Rig rig)
    {
        DesktopLayerTests.Drain();
        rig.Window.UpdateLayout();
        DesktopLayerTests.Drain();
        return (WelcomeView)rig.Driver.Modals.Modal!.View;
    }

    private static object? Focused(Rig rig) => rig.Window.FocusManager?.GetFocusedElement();

    /// <summary>
    /// A new player's path by keyboard alone (wave 31): ⏎ in the find card's search shows the list, ↓ moves the raised row
    /// and ⏎ adds it — the pick the cells' Browse worlds makes — and the form it fills in says which world it is and waits
    /// in Character, the one field the directory could not fill. ⏎ used to ask nothing once the list was shown, and the
    /// keyboard landed in Host, where typing drops the listed name.
    /// </summary>
    [Test]
    public async Task EnterInTheSearchAddsTheRaisedWorld_AndTheFormItFillsWaitsInCharacter()
    {
        var (picked, intro, inCharacter) = await Run(rig =>
        {
            rig.App.RenderSnapshot("welcome");
            var page = WelcomePage(rig);
            page.SearchBox.Focus();
            Press(page.SearchBox, global::Avalonia.Input.Key.Enter);
            Press(page.SearchBox, global::Avalonia.Input.Key.Down);
            Press(page.SearchBox, global::Avalonia.Input.Key.Enter);
            var form = WelcomePage(rig);
            return (rig.App.Welcome?.WorldName, form.FormIntro.IsVisible ? form.FormIntro.Text : null, ReferenceEquals(Focused(rig), form.CharacterBox));
        });

        await Assert.That(picked).IsEqualTo("Lanternport");
        await Assert.That(intro).IsNotNull().And.Contains("Lanternport, as listed on mu*index");
        await Assert.That(inCharacter).IsTrue();
    }

    /// <summary>
    /// A world at several addresses lists them in the find card with the keyboard on the one Enter chooses: ↓ moves it and
    /// Enter chooses the second. The focus used to sit on Back, so Enter after an arrow went back to the worlds.
    /// </summary>
    [Test]
    public async Task TheAddressChoiceHoldsTheKeyboardOnTheAddressEnterChooses()
    {
        var (firstFocused, secondFocused, port) = await Run(rig =>
        {
            rig.App.RenderSnapshot("welcome");
            var page = WelcomePage(rig);
            page.SearchBox.Focus();
            Press(page.SearchBox, global::Avalonia.Input.Key.Enter);
            Press(page.SearchBox, global::Avalonia.Input.Key.Enter);
            page = WelcomePage(rig);
            var first = page.AddressRows.Count > 0 && ReferenceEquals(Focused(rig), page.AddressRows[0]);
            Press((Control)Focused(rig)!, global::Avalonia.Input.Key.Down);
            var second = page.AddressRows.Count > 1 && ReferenceEquals(Focused(rig), page.AddressRows[1]);
            Press((Control)Focused(rig)!, global::Avalonia.Input.Key.Enter);
            WelcomePage(rig);
            return (first, second, rig.App.Welcome?.Port);
        });

        await Assert.That(firstFocused).IsTrue();
        await Assert.That(secondFocused).IsTrue();
        await Assert.That(port).IsEqualTo("4201");
    }

    private static WorldBrowseView BrowsePage(Rig rig)
    {
        DesktopLayerTests.Drain();
        rig.Window.UpdateLayout();
        DesktopLayerTests.Drain();
        return (WorldBrowseView)rig.Driver.Modals.Modal!.View;
    }

    /// <summary>
    /// Esc on a form the find card's Add filled in is one step back (wave 32): the pick is given back and the page is the
    /// list again as it was left — the same row raised, the keyboard in the search — rather than the whole welcome closing.
    /// A second Esc, with nothing left to step back from, closes it.
    /// </summary>
    [Test]
    public async Task EscOnAFormTheFindCardFilledInGivesThePickBack_AndLeavesTheListAsItWasLeft()
    {
        var (open, world, host, rows, raised, inSearch, keys, closedAfter) = await Run(rig =>
        {
            rig.App.RenderSnapshot("welcome");
            var page = WelcomePage(rig);
            page.SearchBox.Focus();
            Press(page.SearchBox, global::Avalonia.Input.Key.Enter);
            Press(page.SearchBox, global::Avalonia.Input.Key.Down);
            Press(page.SearchBox, global::Avalonia.Input.Key.Enter);
            page = WelcomePage(rig);
            var filled = page.Model.WorldName is not null;
            Press(page.CharacterBox!, global::Avalonia.Input.Key.Escape);
            page = WelcomePage(rig);
            var open = filled && rig.App.WelcomeIsOpen;
            var world = rig.App.Welcome?.WorldName;
            var host = page.HostBox!.Text;
            var rows = page.WorldRows.Count;
            var raised = page.SelectedWorldRow;
            var inSearch = ReferenceEquals(Focused(rig), page.SearchBox);
            var keys = page.FooterText;
            Press(page.SearchBox, global::Avalonia.Input.Key.Escape);
            return (open, world, host, rows, raised, inSearch, keys, !rig.App.WelcomeIsOpen);
        });

        await Assert.That(open).IsTrue().Because("Esc on the filled form steps back; it does not close the welcome");
        await Assert.That(world).IsNull();
        await Assert.That(host).IsEqualTo(string.Empty).Because("the form keeps nothing of the pick, as the cells' Esc back to Browse worlds");
        await Assert.That(rows).IsGreaterThan(1);
        await Assert.That(raised).IsEqualTo(1).Because("the list is as it was left: the row the player added is still the raised one");
        await Assert.That(inSearch).IsTrue();
        await Assert.That(keys).EndsWith("Esc closes");
        await Assert.That(closedAfter).IsTrue();
    }

    /// <summary>
    /// The find card's Browse all worlds opens Browse worlds from the welcome, so the list says how to go back (a Back
    /// button and Esc, the cells' Esc on that list) and both end in the welcome again; a pick there fills in the form, and
    /// Esc on that form goes back to the list as it was left — order and row — rather than closing everything.
    /// </summary>
    [Test]
    public async Task BrowseWorldsFromTheWelcomeGoesBackToIt_AndAFormItFilledInGoesBackToTheList()
    {
        var (back, footer, escWelcome, buttonWelcome, order, selected, againBack) = await Run(rig =>
        {
            rig.App.RenderSnapshot("welcome");
            var page = WelcomePage(rig);
            page.BrowseAllButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var browse = BrowsePage(rig);
            var hasBack = browse.BackToWelcomeButton is { IsVisible: true };
            var footer = browse.FooterText;
            Press(browse.Box, global::Avalonia.Input.Key.Escape);
            WelcomePage(rig);
            var escWelcome = rig.App.WelcomeIsOpen && !rig.App.WorldBrowseOpen;

            WelcomePage(rig).BrowseAllButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            BrowsePage(rig).BackToWelcomeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WelcomePage(rig);
            var buttonWelcome = rig.App.WelcomeIsOpen && !rig.App.WorldBrowseOpen;

            // Sorted by name, the second row: a pick, then the address if it asks for one, then Esc on the form.
            WelcomePage(rig).BrowseAllButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            browse = BrowsePage(rig);
            Press(browse.Box, global::Avalonia.Input.Key.Tab);
            Press(browse.Box, global::Avalonia.Input.Key.Down);
            Press(browse.Box, global::Avalonia.Input.Key.Enter);
            if (rig.App.WorldBrowseOpen && BrowsePage(rig).ChoosingAddress)
            {
                Press(BrowsePage(rig).Box, global::Avalonia.Input.Key.Enter);
            }

            page = WelcomePage(rig);
            Press(page.CharacterBox!, global::Avalonia.Input.Key.Escape);
            browse = BrowsePage(rig);
            return (hasBack, footer, escWelcome, buttonWelcome, browse.Order, browse.Selected, browse.BackToWelcomeButton is not null);
        });

        await Assert.That(back).IsTrue();
        await Assert.That(footer).EndsWith("Esc back to the welcome");
        await Assert.That(escWelcome).IsTrue();
        await Assert.That(buttonWelcome).IsTrue();
        await Assert.That(order).IsEqualTo(Core.WorldIndex.WorldDirectorySort.Name).Because("the list comes back as it was left");
        await Assert.That(selected).IsEqualTo(1);
        await Assert.That(againBack).IsTrue().Because("the list is still the welcome's, so its Esc still goes back there");
    }

    /// <summary>
    /// The welcome fits its window with nothing to scroll (wave 32): at the mockup's 1440 × 900 with a pick's form filled
    /// in (the tallest the page gets) at the mockup's spacing, and at the window's default size laid out tight.
    /// </summary>
    [Test]
    [Arguments(1440, 900, false)]
    [Arguments(1230, 822, true)]
    public async Task TheWelcomeFitsItsWindowWithNothingToScroll(int width, int height, bool tight)
    {
        var (fits, compact) = await Run(rig =>
        {
            rig.Window.Width = width;
            rig.Window.Height = height;
            rig.App.RenderSnapshot("welcome");
            var page = WelcomePage(rig);
            page.SearchBox.Focus();
            Press(page.SearchBox, global::Avalonia.Input.Key.Enter);
            Press(page.SearchBox, global::Avalonia.Input.Key.Enter);
            page = WelcomePage(rig);
            Press(page.AddressRows[1], global::Avalonia.Input.Key.Enter);
            page = WelcomePage(rig);
            WelcomePage(rig);
            return (page.FitsWithoutScrolling, page.Compact);
        });

        await Assert.That(fits).IsTrue();
        await Assert.That(compact).IsEqualTo(tight);
    }
}
