using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The desktop's settings window (<see cref="SettingsWindow"/>), generated from the declared sections: one native
/// control per field, every edit committed through the form (and so the client's funnel), reset back to the default,
/// the search narrowing the page and counting matches per section, and the meter table's live <c>now</c> column.
/// </summary>
public class SettingsWindowTests
{
    private sealed class Harness
    {
        public AppConfiguration Config { get; } = new();

        public int Commits { get; set; }

        public List<string> Opened { get; } = [];

        /// <summary>The sections the form lists; every built-in one is declared now, so a test adds one that is not.</summary>
        public IReadOnlyList<SettingsSectionContribution> Sections { get; init; } = BuiltInSettings.All;

        /// <summary>The notes the client captured for the world fields (F5's join wraps recommendation).</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Notes { get; init; } =
            new Dictionary<string, IReadOnlyDictionary<string, string>>();

        public SettingsForm Form(string initial = "text")
        {
            SettingsForm? form = null;
            form = new SettingsForm(
                Sections,
                Config,
                change =>
                {
                    change();
                    Commits++;
                    form!.NotifyChanged();
                },
                Opened.Add,
                initial,
                isDark: true,
                () => [new ReceivedValue("Char.Vitals.hp", "412"), new ReceivedValue("Char.Vitals.maxhp", "500")],
                meter => meter.Value == "Char.Vitals.hp" ? "412/500" : null,
                () => ThemePreviews.Cards(Config),
                () => ThemePreviews.IsDark(ThemeChoices.Resolve(Config)))
            {
                WorldNotes = Notes,
            };
            return form;
        }
    }

    [Test]
    public async Task TheWindowBuildsOneControlPerDeclaredField()
    {
        var (ids, controls) = await Run(new Harness(), "text", window =>
            (window.ShownFields.ToArray(), window.FieldControls.ToDictionary(c => c.Key, c => c.Value.GetType())));

        var declared = BuiltInSettings.Text.Fields.Select(f => f.Id).ToArray();
        await Assert.That(ids).IsEquivalentTo(declared);
        await Assert.That(controls.Keys).IsEquivalentTo(declared);
        await Assert.That(controls["legible"]).IsEqualTo(typeof(ToggleSwitch));
        await Assert.That(controls["tab-width"]).IsEqualTo(typeof(NumericUpDown));
        await Assert.That(controls[BuiltInSettings.MetersField]).IsEqualTo(typeof(Grid));
    }

    [Test]
    public async Task AnEditCommitsThroughTheFormAndResetPutsTheDefaultBack()
    {
        var harness = new Harness();
        var fresh = new AppConfiguration().Text.KeepTextLegible;
        var (afterEdit, resetShown, afterReset, resetHidden) = await Run(harness, "text", window =>
        {
            var toggle = (ToggleSwitch)window.FieldControls["legible"];
            toggle.IsChecked = !fresh;
            Dispatcher.UIThread.RunJobs();
            var edited = harness.Config.Text.KeepTextLegible;
            var shown = window.ResetButtons["legible"].IsVisible;

            window.ResetButtons["legible"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            return (edited, shown, harness.Config.Text.KeepTextLegible, !window.ResetButtons["legible"].IsVisible && toggle.IsChecked == fresh);
        });

        await Assert.That(afterEdit).IsEqualTo(!fresh);
        await Assert.That(resetShown).IsTrue().Because("a changed field carries its reset");
        await Assert.That(afterReset).IsEqualTo(fresh);
        await Assert.That(resetHidden).IsTrue().Because("the toggle shows the default again and the reset goes");
        await Assert.That(harness.Commits).IsEqualTo(2);
    }

    [Test]
    public async Task ANumberIsCommittedWithinItsBounds()
    {
        var harness = new Harness();
        var wanted = new AppConfiguration().Input.Rows + 1;
        await Run(harness, "input", window =>
        {
            ((NumericUpDown)window.FieldControls["rows"]).Value = 999;
            Dispatcher.UIThread.RunJobs();
            ((NumericUpDown)window.FieldControls["rows"]).Value = wanted;
            Dispatcher.UIThread.RunJobs();
            return 0;
        });

        await Assert.That(harness.Config.Input.Rows).IsEqualTo(wanted);
        await Assert.That(harness.Commits).IsEqualTo(2).Because("999 was clipped to the maximum and committed, then the value");
    }

    [Test]
    public async Task TheSearchNarrowsThePageAndCountsEachSection()
    {
        var (fields, badges, section) = await Run(new Harness(), "input", window =>
        {
            window.SearchBox.Text = "volume";
            Dispatcher.UIThread.RunJobs();
            return (window.ShownFields.ToArray(), window.SectionBadges, window.ShownSection);
        });

        var expected = SettingsSearch.Find(BuiltInSettings.All, "volume");
        await Assert.That(section).IsEqualTo("text").Because("the section on show had nothing matching, so the first that has is shown");
        await Assert.That(fields).IsEquivalentTo(expected.Where(m => m.Section.Id == "text").Select(m => m.Field!.Id).ToArray());
        await Assert.That(badges["text"]).IsEqualTo(expected.Count(m => m.Section.Id == "text").ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(badges["input"]).IsEqualTo(string.Empty).Because("a section with no match says nothing, as the mockup's rows do");
    }

    [Test]
    public async Task TheMeterTableHasARowPerMeterAndReadsNow()
    {
        var harness = new Harness();
        harness.Config.Text.StatusMeters = [new StatusMeter { Label = "hp", Value = "Char.Vitals.hp", Max = "Char.Vitals.maxhp" }];
        var (rows, now, rowsAfterAdd) = await Run(harness, "text", window =>
        {
            var grid = window.MeterGrid!;
            var rows = grid.RowDefinitions.Count;
            var now = grid.Children.OfType<TextBlock>().Where(t => Grid.GetRow(t) == 1 && Grid.GetColumn(t) == 3).Select(t => t.Text).Single();

            var add = window.GetVisualDescendantsOf<Button>().Single(b => Equals(b.Content, "+ Add a meter"));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            return (rows, now, window.MeterGrid!.RowDefinitions.Count);
        });

        await Assert.That(rows).IsEqualTo(2).Because("a heading row and one meter");
        await Assert.That(now).IsEqualTo("412/500");
        await Assert.That(rowsAfterAdd).IsEqualTo(3);
        await Assert.That(harness.Config.Text.StatusMeters.Select(m => m.Label)).IsEquivalentTo(new[] { "hp", "new" });
    }

    [Test]
    public async Task AnUndeclaredSectionOpensItsOwnScreen()
    {
        var harness = new Harness
        {
            Sections = [.. BuiltInSettings.All, new SettingsSectionContribution { Id = "elsewhere", Title = "Elsewhere", Key = "F11", Order = 95 }],
        };
        var shown = await Run(harness, "text", window =>
        {
            var item = window.SectionList.Items.OfType<ListBoxItem>().Single(i => Equals(i.Tag, "elsewhere"));
            window.SectionList.SelectedItem = item;
            Dispatcher.UIThread.RunJobs();
            return window.ShownSection;
        });

        await Assert.That(harness.Opened).IsEquivalentTo(new[] { "elsewhere" });
        await Assert.That(shown).IsEqualTo("text");
    }

    /// <summary>The theme field is a card per theme, the one in use marked; a click uses another and the mark follows.</summary>
    [Test]
    public async Task TheThemeFieldIsCardsAndAClickUsesTheTheme()
    {
        var harness = new Harness();
        var (names, before, after, dark) = await Run(harness, "text", window =>
        {
            var cards = window.ThemeCards;
            var names = cards.Select(c => c.Card.Name).ToArray();
            var before = cards.Single(c => c.InUse).Card.Name;
            cards.Single(c => c.Card.Name == "Light").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            return (names, before, window.ThemeCards.Single(c => c.InUse).Card.Name, window.RequestedThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark);
        });

        await Assert.That(names).IsEquivalentTo(ThemeChoices.Names(new AppConfiguration()));
        await Assert.That(before).IsEqualTo("Dark");
        await Assert.That(harness.Config.ThemeName).IsEqualTo("Light");
        await Assert.That(harness.Commits).IsEqualTo(1);
        await Assert.That(after).IsEqualTo("Light");
        await Assert.That(dark).IsFalse();
    }

    private static Harness TwoWorlds()
    {
        var harness = new Harness();
        harness.Config.Worlds.Clear();
        harness.Config.Worlds.Add(new WorldDefinition { Name = "Aetherfall", Host = "aether.example", Characters = { new CharacterDefinition { Name = "Corvid" } } });
        harness.Config.Worlds.Add(new WorldDefinition { Name = "Brightwater", Host = "bright.example", Characters = { new CharacterDefinition { Name = "Wren" } } });
        return harness;
    }

    /// <summary>F5's section is drawn here: a world picker over the declared fields, and an edit lands on the world picked.</summary>
    [Test]
    public async Task AWorldSectionPicksItsWorldAndEditsIt()
    {
        var harness = TwoWorlds();
        var (fields, subjects) = await Run(harness, "worlds", window =>
        {
            var subjects = window.SubjectPicker!.ItemCount;
            window.SubjectPicker.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            var box = (TextBox)window.FieldControls["edit-grab"];
            box.Text = "Edit>";
            box.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter });
            Dispatcher.UIThread.RunJobs();
            return (window.ShownFields.ToArray(), subjects);
        });

        await Assert.That(subjects).IsEqualTo(2);
        await Assert.That(fields).IsEquivalentTo(BuiltInSettings.World.Fields.Select(f => f.Id).ToArray());
        await Assert.That(harness.Config.Worlds[1].GrabPrefix).IsEqualTo("Edit>");
        await Assert.That(harness.Config.Worlds[0].GrabPrefix).IsNull();
        await Assert.That(harness.Commits).IsEqualTo(1);
    }

    /// <summary>
    /// The cell F5's <c>join wraps</c> recommendation is drawn under the field natively, from the same sentence, and goes
    /// when the field is changed.
    /// </summary>
    [Test]
    public async Task JoinWrapsCarriesTheCellScreensRecommendation()
    {
        var advice = WorldFieldValues.JoinWrapsAdvice(78, 60);
        var harness = new Harness
        {
            Notes = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                [WorldFieldValues.JoinWrapsFieldId] = new Dictionary<string, string> { ["Aetherfall"] = advice },
            },
        };
        harness.Config.Worlds.Clear();
        harness.Config.Worlds.Add(new WorldDefinition { Name = "Aetherfall", Host = "aether.example" });
        var (before, shownBefore, after) = await Run(harness, "worlds", window =>
        {
            var note = window.NoteFor(WorldFieldValues.JoinWrapsFieldId)!;
            var text = note.Text;
            var shown = note.IsVisible;
            var combo = (ComboBox)window.FieldControls[WorldFieldValues.JoinWrapsFieldId];
            combo.SelectedItem = WorldFieldValues.JoinWrapChoices[1];
            Dispatcher.UIThread.RunJobs();
            return (text, shown, window.NoteFor(WorldFieldValues.JoinWrapsFieldId)!.IsVisible);
        });

        await Assert.That(before).IsEqualTo("Recommended · wraps at 78, pane is 60");
        await Assert.That(shownBefore).IsTrue();
        await Assert.That(harness.Config.Worlds[0].JoinWrappedLines).IsTrue();
        await Assert.That(after).IsFalse();
    }

    /// <summary>A text the cell screen would refuse is refused here too, and the box shows the value kept.</summary>
    [Test]
    public async Task ARefusedTextIsPutBack()
    {
        var harness = TwoWorlds();
        var shown = await Run(harness, "worlds", window =>
        {
            var box = (TextBox)window.FieldControls["host"];
            box.Text = "   ";
            box.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter });
            Dispatcher.UIThread.RunJobs();
            return box.Text;
        });

        await Assert.That(shown).IsEqualTo("aether.example");
        await Assert.That(harness.Config.Worlds[0].Host).IsEqualTo("aether.example");
        await Assert.That(harness.Commits).IsEqualTo(0);
    }

    /// <summary>The lists F5 holds (characters, trigger sets, adding and deleting) are its cell screen's; the door opens it.</summary>
    [Test]
    public async Task TheWorldSectionsDoorOpensTheCellScreen()
    {
        var harness = TwoWorlds();
        await Run(harness, "worlds", window =>
        {
            window.ScreenDoor!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            return 0;
        });

        await Assert.That(harness.Opened).IsEquivalentTo(new[] { "worlds" });
    }

    /// <summary>With no world there is nothing to edit: no fields, a sentence saying so, and the door to F5.</summary>
    [Test]
    public async Task AWorldSectionWithNoWorldsShowsOnlyTheDoor()
    {
        var harness = new Harness();
        harness.Config.Worlds.Clear();
        var (fields, door, picker) = await Run(harness, "worlds", window =>
            (window.ShownFields.Count, window.ScreenDoor is not null, window.SubjectPicker is not null));

        await Assert.That(fields).IsEqualTo(0);
        await Assert.That(door).IsTrue();
        await Assert.That(picker).IsFalse();
    }

    /// <summary>F7 in the window host reaches the presenter, which opens the settings window on that section.</summary>
    [Test]
    public async Task F7InTheWindowHostOpensTheSettingsWindow()
    {
        var section = await Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(120, 34);
            var app = new SharpMUTermApp(DemoScene.Build(), WindowHost.Create(driver, clipboard: null) with
            {
                Capabilities = WindowOptions.Capabilities,
                Audio = null,
                Presence = null,
                Announcer = null,
                RunsInline = true,
            });
            try
            {
                app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.F8, false, false, false));
                Dispatcher.UIThread.RunJobs();
                var window = driver.Settings.Window;
                var shown = window?.ShownSection;
                window?.Close();
                return (shown, app.SettingsOpenKey);
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }, CancellationToken.None);

        await Assert.That(section.shown).IsEqualTo("input");
        await Assert.That(section.SettingsOpenKey).IsNull().Because("no cell screen opens under it");
    }

    /// <summary>
    /// F7 in the window takes the keyboard: the settings window's search box has focus, so typing goes there and not
    /// into the command line behind it, where ⏎ would send it. Closing gives the keyboard back to the cells.
    /// </summary>
    [Test]
    public async Task TheSettingsWindowTakesTheKeyboardAndGivesItBack()
    {
        var (searchFocused, typed, line, viewFocused) = await Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(120, 34);
            var view = new TerminalView(driver, SkiaSharp.SKColors.White, new SkiaSharp.SKColor(0x1e, 0x1e, 0x24), 14, null);
            var main = new Window { Content = view, Width = 1000, Height = 700 };
            driver.Settings.Owner = main;
            driver.Settings.Refocus = () => view.Focus();
            main.Show();
            view.Focus();
            var app = new SharpMUTermApp(DemoScene.Build(), WindowHost.Create(driver, clipboard: null) with
            {
                Capabilities = WindowOptions.Capabilities,
                Audio = null,
                Presence = null,
                Announcer = null,
                RunsInline = true,
            });
            driver.KeyPressed += (_, key) => app.SimulateKey(key);
            try
            {
                var before = app.ArmedInputText;
                app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.F7, false, false, false));
                Dispatcher.UIThread.RunJobs();
                var window = driver.Settings.Window!;
                var focused = window.SearchBox.IsFocused;
                window.KeyTextInput("theme");
                Dispatcher.UIThread.RunJobs();
                var typed = window.SearchBox.Text;
                var line = app.ArmedInputText == before ? string.Empty : app.ArmedInputText;
                window.Close();
                Dispatcher.UIThread.RunJobs();
                return (focused, typed, line, view.IsFocused);
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                main.Close();
            }
        }, CancellationToken.None);

        await Assert.That(searchFocused).IsTrue();
        await Assert.That(typed).IsEqualTo("theme").Because("what is typed after F7 goes into the settings window");
        await Assert.That(line).IsEmpty().Because("nothing reached the command line behind it");
        await Assert.That(viewFocused).IsTrue().Because("closing gives the keyboard back to the cells, and so the command line");
    }

    private static Task<T> Run<T>(Harness harness, string initial, Func<SettingsWindow, T> body) =>
        Session.Dispatch(() =>
        {
            var window = new SettingsWindow(harness.Form(initial));
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                return body(window);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

    private static HeadlessUnitTestSession Session => TerminalViewTests.Session;
}

internal static class VisualExtensions
{
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this global::Avalonia.Visual visual) =>
        global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(visual).OfType<T>();
}
