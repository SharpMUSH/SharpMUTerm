using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Telnet;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Tui;
using SharpMUTerm.Tui.Tests;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window's composer, driven from the real client in the window host over a connected session: F1 opens the native
/// editor over the cells, typing and ⌃S send exactly what the cell composer sends, Esc keeps the draft, the wavy lines
/// are the model's marks, ⌥W and the right-click menu reach the dictionary, a MOO edit saves its lines, and the docked
/// composer's editor arms, sends and releases through the client's one fact.
/// </summary>
[NotInParallel]
public class NativeComposerTests
{
    private static readonly MooEdit Look = new("#98.look", "Test:look", "moo-code", ["player:tell(\"Hi.\");", "return 1;"]);

    private sealed record Rig(Window Window, TerminalView View, DesktopLayer Layer, SharpMUTermApp App, AvaloniaConsoleDriver Driver, RecordingTelnetSession Telnet);

    private static SpellChecker Dictionary() =>
        SpellChecker.FromStreams(new MemoryStream("5\nhello\nthe\nworld\nwaves\nsits\n"u8.ToArray()), new MemoryStream("SET UTF-8\n"u8.ToArray()), "en_GB");

    private static Task<T> Run<T>(Func<Rig, T> body, bool spelling = false) =>
        Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(120, 34);
            var view = new TerminalView(driver, SKColors.White, new SKColor(0x1e, 0x1e, 0x24), 14, null);
            var root = new Panel();
            root.Children.Add(view);
            var layer = new DesktopLayer(view, HostChrome.HeaderAndRail);
            root.Children.Add(layer);
            driver.Composer.Attach(root, layer, view, layer.FocusArmedComposer);
            var window = new Window { Content = root, Width = 1100, Height = 760 };
            window.Show();
            view.Focus();

            var config = new AppConfiguration();
            var world = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
            world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
            config.Worlds.Add(world);
            var checker = spelling ? Dictionary() : null;
            var app = new SharpMUTermApp(
                config,
                WindowHost.Create(driver, clipboard: null) with
                {
                    Audio = null,
                    Presence = null,
                    Announcer = null,
                    NativeSettings = null,
                    NativePalette = null,
                    RunsInline = true,
                },
                spelling: checker is null ? null : () => checker);
            var telnet = new RecordingTelnetSession();
            app.TelnetFactory = _ => telnet;

            // No loop runs here, so a key the window hands the driver is delivered the way the loop would deliver it.
            driver.KeyPressed += (_, key) => app.SimulateKey(key);
            try
            {
                app.BindWorldWithoutConnecting(world).ConnectAsync().GetAwaiter().GetResult();
                app.RenderNextFrame();
                Drain();
                return body(new Rig(window, view, layer, app, driver, telnet));
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

    private static void Drain()
    {
        DesktopLayerTests.Drain();
        DesktopLayerTests.Drain();
    }

    private static ComposerOverlay OpenF1(Rig rig)
    {
        rig.App.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false));
        Drain();
        rig.Window.UpdateLayout();
        Drain();
        return rig.Driver.Composer.Overlay ?? throw new InvalidOperationException("F1 did not open in the window");
    }

    private static void Type(ComposerEditor editor, string text)
    {
        editor.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = editor });
        Drain();
    }

    private static void Press(ComposerEditor editor, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = editor });
        Drain();
    }

    // ── F1 ───────────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task F1OpensTheNativeEditorFocusedOverTheCellsAndNotTheCellModal()
    {
        var (focused, cellOpen, header, footer, below, margin) = await Run(rig =>
        {
            var overlay = OpenF1(rig);
            var root = (Panel)rig.Window.Content!;
            return (overlay.Composer.Editor.IsFocused, rig.App.Composer.IsOpen, overlay.Composer.HeaderText, overlay.Composer.FooterText,
                root.Children.IndexOf(overlay) == root.Children.IndexOf(rig.Layer) - 1, overlay.Margin == rig.View.Margin);
        });

        await Assert.That(focused).IsTrue();
        await Assert.That(cellOpen).IsFalse();
        await Assert.That(header).Contains("COMPOSE").And.Contains("to Ann").And.Contains("as typed").And.Contains("no dictionary");
        await Assert.That(footer).Contains("Ctrl+S send").And.Contains("Alt+L escaping").And.Contains("Esc close (keeps the draft)");
        await Assert.That(below).IsTrue().Because("under the layer, so a toast is drawn over it");
        await Assert.That(margin).IsTrue().Because("over the cells, clear of the native header and sidebar");
    }

    /// <summary>
    /// With no dictionary, the header's chip is the way to one: a click opens the offer ⌥W opens, and fetches nothing.
    /// </summary>
    [Test]
    public async Task TheNoDictionaryChipOpensTheOffer()
    {
        var (text, tip, offered) = await Run(rig =>
        {
            var overlay = OpenF1(rig);
            var chip = overlay.Composer.DictionaryChip(12);
            var label = ((TextBlock)chip.Child!).Text;
            var tooltip = ToolTip.GetTip(chip) as string;
            var pointer = new Pointer(0, PointerType.Mouse, true);
            var point = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            chip.RaiseEvent(new PointerPressedEventArgs(chip, pointer, chip, default, 0, point, KeyModifiers.None));
            Drain();
            return (label, tooltip, rig.App.DictionaryOfferOpen);
        });

        await Assert.That(text).IsEqualTo(ComposerView.NoDictionaryChip);
        await Assert.That(tip).Contains("Alt+W");
        await Assert.That(offered).IsTrue();
    }

    [Test]
    [Arguments("+pose waves\nand sits", false)]
    [Arguments("+bbpost 1=Title\n\nFirst, 50% [done].\n", true)]
    public async Task TypingAndCtrlSSendWhatTheCellComposerSends(string draft, bool literal)
    {
        var (sent, open) = await Run(rig =>
        {
            var overlay = OpenF1(rig);
            var editor = overlay.Composer.Editor;
            Type(editor, draft);
            if (literal)
            {
                Press(editor, Key.L, KeyModifiers.Alt);
            }

            Press(editor, Key.S, KeyModifiers.Control);
            return (rig.Telnet.Lines.ToList(), rig.Driver.Composer.Overlay is not null);
        });

        await Assert.That(sent).IsEquivalentTo(new[] { ComposeMessage.Build(draft, literal ? ComposeEscaping.Literal : ComposeEscaping.AsTyped) });
        await Assert.That(open).IsFalse().Because("a send closes it");
    }

    [Test]
    public async Task AltLFlipsTheHeadersModeFromTheClientsModel()
    {
        var header = await Run(rig =>
        {
            var overlay = OpenF1(rig);
            Press(overlay.Composer.Editor, Key.L, KeyModifiers.Alt);
            return overlay.Composer.HeaderText;
        });

        await Assert.That(header).Contains("literal");
    }

    [Test]
    public async Task TheHeaderCountsThePostAsSentAndAltPShowsIt()
    {
        var (count, previewShown, preview, hidden) = await Run(rig =>
        {
            var overlay = OpenF1(rig);
            Type(overlay.Composer.Editor, "+post one");
            var count = overlay.Composer.CountText;
            Press(overlay.Composer.Editor, Key.P, KeyModifiers.Alt);
            Drain();
            var reopened = rig.Driver.Composer.Overlay!;
            var shown = reopened.Composer.PreviewText is { Length: > 0 } && rig.App.Composer.Preview;
            var text = reopened.Composer.PreviewText;
            Press(reopened.Composer.Editor, Key.P, KeyModifiers.Alt);
            Drain();
            return (count, shown, text, !rig.App.Composer.Preview);
        });

        await Assert.That(count).IsEqualTo(ComposerModel.CharactersLabel("+post one".Length));
        await Assert.That(previewShown).IsTrue();
        await Assert.That(preview).Contains("+post one");
        await Assert.That(hidden).IsTrue().Because("⌥P again hides it");
    }

    [Test]
    public async Task EscKeepsTheDraftForTheNextF1()
    {
        var (closed, sent, reopened, focusBack) = await Run(rig =>
        {
            var first = OpenF1(rig);
            Type(first.Composer.Editor, "half a post");
            Press(first.Composer.Editor, Key.Escape);
            var closed = rig.Driver.Composer.Overlay is null;
            var focus = rig.View.IsFocused;
            var second = OpenF1(rig);
            return (closed, rig.Telnet.Lines.ToList(), second.Composer.Editor.Text, focus);
        });

        await Assert.That(closed).IsTrue();
        await Assert.That(sent).IsEmpty();
        await Assert.That(reopened).IsEqualTo("half a post");
        await Assert.That(focusBack).IsTrue().Because("the keyboard goes back to the cells");
    }

    [Test]
    public async Task CtrlEnterAndShiftEnterAreNewlinesAndAltEnterIsTheWindows()
    {
        var text = await Run(rig =>
        {
            var editor = OpenF1(rig).Composer.Editor;
            Type(editor, "one");
            Press(editor, Key.Enter, KeyModifiers.Control);
            Type(editor, "two");
            Press(editor, Key.Enter, KeyModifiers.Shift);
            Type(editor, "three");
            return editor.Text;
        });

        await Assert.That(text).IsEqualTo("one\ntwo\nthree");
    }

    // ── spelling ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task TheWavyLinesAreTheModelsMarks()
    {
        var (marks, model, cell, rects, header) = await Run(
            rig =>
            {
                var overlay = OpenF1(rig);
                Type(overlay.Composer.Editor, "hello wrold\nthe waves zork");
                rig.Window.UpdateLayout();
                Drain();
                var session = overlay.Session;
                var text = overlay.Composer.Editor.Text!;
                return (overlay.Composer.Editor.Marks.ToList(), session.Model.Marks.ToList(),
                    ComposeOverlay.ModelFor("Ann", text, ComposeEscaping.AsTyped, null, Dictionary(), true).Marks.ToList(),
                    overlay.Composer.Editor.MarkRects().Count, overlay.Composer.HeaderText);
            },
            spelling: true);

        await Assert.That(marks).IsEquivalentTo(new[] { new SpellRange(6, 5), new SpellRange(22, 4) });
        await Assert.That(marks).IsEquivalentTo(model);
        await Assert.That(marks).IsEquivalentTo(cell).Because("the cell composer marks the same words");
        await Assert.That(rects).IsEqualTo(2).Because("each mark is a rectangle of the laid-out text, so a line is drawn under it");
        await Assert.That(header).Contains("en_GB");
    }

    [Test]
    public async Task AltWAddsTheWordAtTheCaret()
    {
        var marks = await Run(
            rig =>
            {
                var editor = OpenF1(rig).Composer.Editor;
                Type(editor, "hello wrold");
                Press(editor, Key.W, KeyModifiers.Alt);
                return editor.Marks.ToList();
            },
            spelling: true);

        await Assert.That(marks).IsEmpty();
    }

    [Test]
    public async Task TheRightClickMenuOffersSuggestionsAndReplacesTheWord()
    {
        var (headers, text) = await Run(
            rig =>
            {
                var composer = OpenF1(rig).Composer;
                Type(composer.Editor, "hello wrold");
                var menu = composer.OpenMenu(8);
                var items = menu.ItemsSource!.OfType<MenuItem>().ToList();
                var headers = items.Select(i => i.Header?.ToString() ?? string.Empty).ToList();
                items.First(i => (string?)i.Header == "world").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.Close();
                Drain();
                return (headers, composer.Editor.Text);
            },
            spelling: true);

        await Assert.That(headers).Contains("world");
        await Assert.That(headers).Contains("Add “wrold” to dictionary");
        await Assert.That(headers).Contains("Proportional font");
        await Assert.That(text).IsEqualTo("hello world");
    }

    // ── MOO edits ────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AMooEditOpensNativelyAndCtrlSSavesItsLines()
    {
        var (header, footer, saved, sent) = await Run(rig =>
        {
            rig.Telnet.ReceiveMooEdit(Look);
            Drain();
            var overlay = rig.Driver.Composer.Overlay!;
            var editor = overlay.Composer.Editor;
            editor.CaretIndex = editor.Text!.Length;
            Type(editor, "\n// 100% [sure]; done\n");
            var header = overlay.Composer.HeaderText;
            var footer = overlay.Composer.FooterText;
            Press(editor, Key.S, KeyModifiers.Control);
            return (header, footer, rig.Telnet.SavedEdits.Single().Lines.ToList(), rig.Telnet.Lines.ToList());
        });

        await Assert.That(header).Contains("EDIT").And.Contains("Test:look").And.DoesNotContain("as typed");
        await Assert.That(footer).Contains("Ctrl+S save").And.DoesNotContain("Alt+L");
        await Assert.That(saved).IsEquivalentTo(new[] { "player:tell(\"Hi.\");", "return 1;", "// 100% [sure]; done" });
        await Assert.That(sent).IsEmpty();
    }

    // ── the docked composer ──────────────────────────────────────────────────────────────────

    private static ComposerView Dock(Rig rig)
    {
        rig.App.DispatchCommand("panel:composer");
        rig.App.RenderNextFrame();
        rig.App.RenderNextFrame();
        Drain();
        rig.Window.UpdateLayout();
        Drain();
        return rig.Layer.Composers.Values.Single();
    }

    [Test]
    public async Task TheDockedEditorLiesOverItsPaneAndTakesTheKeyboardWhenArmed()
    {
        var (focused, armed, header, footer, width) = await Run(rig =>
        {
            var composer = Dock(rig);
            return (composer.Editor.IsFocused, composer.Model!.Armed, composer.HeaderText, composer.FooterText, composer.Bounds.Width);
        });

        await Assert.That(armed).IsTrue();
        await Assert.That(focused).IsTrue();
        await Assert.That(header).Contains("Compose").And.Contains("for Ann");
        await Assert.That(footer).Contains("Ctrl+S").And.Contains("Esc");
        await Assert.That(width).IsGreaterThan(0);
    }

    [Test]
    public async Task TypingInTheDockedEditorIsTheDraftAndCtrlSSendsIt()
    {
        var (draft, sent, after) = await Run(rig =>
        {
            var composer = Dock(rig);
            var windowId = composer.Session!.Model.Place == ComposerPlace.Docked ? rig.Layer.Composers.Keys.Single() : string.Empty;
            Type(composer.Editor, "+pose waves\nand sits");
            var draft = rig.App.DockedComposerIn(windowId)!.Body;
            Press(composer.Editor, Key.S, KeyModifiers.Control);
            rig.App.RenderNextFrame();
            Drain();
            return (draft, rig.Telnet.Lines.ToList(), rig.Layer.Composers.Values.Single().Editor.Text);
        });

        await Assert.That(draft).IsEqualTo("+pose waves\nand sits");
        await Assert.That(sent).IsEquivalentTo(new[] { "+pose waves%rand sits" });
        await Assert.That(after).IsEqualTo(string.Empty).Because("the client cleared the draft, and the editor takes the client's text");
    }

    [Test]
    public async Task EscReleasesToTheCellsAndAClickArmsAgain()
    {
        var (releasedArmed, viewFocused, rearmed, editorFocused) = await Run(rig =>
        {
            var composer = Dock(rig);
            Press(composer.Editor, Key.Escape);
            rig.App.RenderNextFrame();
            Drain();
            var released = rig.App.ComposerArmed;
            var view = rig.View.IsFocused;

            var centre = composer.Editor.TranslatePoint(new Point(composer.Editor.Bounds.Width / 2, composer.Editor.Bounds.Height / 2), rig.Window)!.Value;
            rig.Window.MouseDown(centre, MouseButton.Left);
            rig.Window.MouseUp(centre, MouseButton.Left);
            Drain();
            rig.App.RenderNextFrame();
            Drain();
            return (released, view, rig.App.ComposerArmed, rig.Layer.Composers.Values.Single().Editor.IsKeyboardFocusWithin);
        });

        await Assert.That(releasedArmed).IsFalse();
        await Assert.That(viewFocused).IsTrue();
        await Assert.That(rearmed).IsTrue();
        await Assert.That(editorFocused).IsTrue();
    }

    [Test]
    public async Task F1FromTheDockedEditorOpensTheSameDraftFullScreen()
    {
        var (body, docked) = await Run(rig =>
        {
            var composer = Dock(rig);
            Type(composer.Editor, "half a post");
            Press(composer.Editor, Key.F1);
            rig.App.RenderNextFrame();
            Drain();
            return (rig.Driver.Composer.Overlay?.Composer.Editor.Text, rig.Layer.Composers.Count);
        });

        await Assert.That(body).IsEqualTo("half a post");
        await Assert.That(docked).IsEqualTo(0).Because("F1 covers the panes, as the cell modal does");
    }

    [Test]
    public async Task TheDockedEditorHandsTheClientsChordsOnAndKeepsItsOwn()
    {
        await Assert.That(ComposerView.ReachesTheClient(Key.P, KeyModifiers.Control)).IsTrue();
        await Assert.That(ComposerView.ReachesTheClient(Key.Up, KeyModifiers.Control)).IsTrue();
        await Assert.That(ComposerView.ReachesTheClient(Key.J, KeyModifiers.Alt)).IsTrue();
        await Assert.That(ComposerView.ReachesTheClient(Key.F2, KeyModifiers.None)).IsTrue();
        await Assert.That(ComposerView.ReachesTheClient(Key.C, KeyModifiers.Control)).IsFalse();
        await Assert.That(ComposerView.ReachesTheClient(Key.Z, KeyModifiers.Control)).IsFalse();
        await Assert.That(ComposerView.ReachesTheClient(Key.Left, KeyModifiers.Alt)).IsFalse();
        await Assert.That(ComposerView.ReachesTheClient(Key.A, KeyModifiers.None)).IsFalse();
    }

    private static HeadlessUnitTestSession Session => TerminalViewTests.Session;
}
