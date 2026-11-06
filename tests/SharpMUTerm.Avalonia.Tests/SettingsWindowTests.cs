using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.VisualTree;
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

        /// <summary>The client's undo history, when the test wants the form to keep one.</summary>
        public SettingsHistory? History { get; set; }

        public List<string> Opened { get; } = [];

        /// <summary>The session keys the form's connect buttons asked for.</summary>
        public List<string> Connects { get; } = [];

        /// <summary>The commands the worlds page ran (Browse worlds, the import).</summary>
        public List<string> Ran { get; } = [];

        /// <summary>The characters the client says are connected.</summary>
        public List<string> Connected { get; } = [];

        /// <summary>The sections the form lists; every built-in one is declared now, so a test adds one that is not.</summary>
        public IReadOnlyList<SettingsSectionContribution> Sections { get; init; } = BuiltInSettings.All;

        /// <summary>The notes the client captured for the world fields (F5's join wraps recommendation).</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Notes { get; init; } =
            new Dictionary<string, IReadOnlyDictionary<string, string>>();

        /// <summary>Commits land a turn later, as the client's do when its thread is not the window's.</summary>
        public bool Deferred { get; init; }

        public SettingsForm Form(string initial = "text")
        {
            SettingsForm? form = null;
            form = new SettingsForm(
                Sections,
                Config,
                change =>
                {
                    void Land()
                    {
                        change();
                        Commits++;
                        form!.NotifyChanged();
                    }

                    if (Deferred)
                    {
                        Dispatcher.UIThread.Post(Land);
                    }
                    else
                    {
                        Land();
                    }
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
                ConnectedKeys = () => Connected.ToArray(),
                Connect = Connects.Add,
                RunCommand = Ran.Add,
                CommitAs = History is null ? null : (change, what) =>
                {
                    var before = History.Capture();
                    change();
                    Commits++;
                    History.Record(form!.Showing ?? initial, before, what);
                    form.HistoryNote = null;
                    form.NotifyChanged();
                },
                UndoRedo = History is null ? null : (section, redo) =>
                {
                    form!.HistoryNote = (redo ? History.Redo(section) : History.Undo(section)).Note;
                    form.NotifyChanged();
                },
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
            var now = grid.Children.OfType<TextBlock>().Where(t => Grid.GetRow(t) == 1 && Grid.GetColumn(t) == 4).Select(t => t.Text).Single();

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
    public async Task AMetersColourIsPickedFromThePaletteAndStoredThroughTheForm()
    {
        var harness = new Harness();
        harness.Config.Text.StatusMeters = [new StatusMeter { Label = "hp", Value = "Char.Vitals.hp", Max = "Char.Vitals.maxhp" }];
        var (first, count) = await Run(harness, "text", window =>
        {
            var box = window.MeterGrid!.Children.OfType<ComboBox>().Single(c => Grid.GetRow(c) == 1 && Grid.GetColumn(c) == 3);
            var first = box.SelectedIndex;
            var count = box.ItemCount;
            box.SelectedIndex = SharpMUTerm.Client.Panels.MeterColours.Choices.ToList().IndexOf("purple");
            Dispatcher.UIThread.RunJobs();
            return (first, count);
        });

        await Assert.That(first).IsEqualTo(0).Because("a meter with no colour of its own reads auto");
        await Assert.That(count).IsEqualTo(SharpMUTerm.Client.Panels.MeterColours.Choices.Count);
        await Assert.That(harness.Config.Text.StatusMeters[0].Colour).IsEqualTo("purple");
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

    [Test]
    public async Task CtrlZOnThePageUndoesTheSectionsEditAndTheFooterSaysSo()
    {
        var harness = new Harness();
        harness.History = new SettingsHistory(harness.Config, id => BuiltInSettings.All.FirstOrDefault(s => s.Id == id)?.Title);
        var fresh = new AppConfiguration().Text.KeepTextLegible;
        var (edited, undone, said, redone, nothing, keys) = await Run(harness, "text", window =>
        {
            var toggle = (ToggleSwitch)window.FieldControls["legible"];
            toggle.IsChecked = !fresh;
            Dispatcher.UIThread.RunJobs();
            var edited = harness.Config.Text.KeepTextLegible;

            Press(window, global::Avalonia.Input.Key.Z, global::Avalonia.Input.KeyModifiers.Control);
            var undone = harness.Config.Text.KeepTextLegible;
            var said = window.FooterNote;

            Press(window, global::Avalonia.Input.Key.Z, global::Avalonia.Input.KeyModifiers.Control | global::Avalonia.Input.KeyModifiers.Shift);
            var redone = harness.Config.Text.KeepTextLegible;

            Press(window, global::Avalonia.Input.Key.Z, global::Avalonia.Input.KeyModifiers.Control);
            Press(window, global::Avalonia.Input.Key.Z, global::Avalonia.Input.KeyModifiers.Control);
            return (edited, undone, said, redone, window.FooterNote, SettingsWindow.FooterKeys);
        });

        await Assert.That(edited).IsEqualTo(!fresh);
        await Assert.That(undone).IsEqualTo(fresh).Because("Ctrl+Z undid the switch");
        await Assert.That(said).StartsWith("Undid");
        await Assert.That(redone).IsEqualTo(!fresh).Because("Ctrl+Shift+Z made it again");
        await Assert.That(nothing).IsEqualTo($"Nothing to undo in {BuiltInSettings.Text.Title}.").Because("refused out loud");
        await Assert.That(keys).StartsWith("Undo Ctrl+Z").Because("the mockup's footer");
    }

    /// <summary>
    /// Ctrl+Z in a focused text box is the box's only while the box has typing of its own to undo: a box holding the value
    /// the page put in it hands the chord to the section's undo, and a box typed in undoes its typing first, then hands
    /// the next Ctrl+Z to the section.
    /// </summary>
    [Test]
    public async Task CtrlZInATextBoxIsTheSectionsUndoUntilTheBoxHasTypingOfItsOwn()
    {
        var harness = new Harness();
        harness.History = new SettingsHistory(harness.Config, id => BuiltInSettings.All.FirstOrDefault(s => s.Id == id)?.Title);
        var fresh = new AppConfiguration().Text.KeepTextLegible;
        var (untouchedUndid, typed, typingUndone, legibleAfterTyping, thenSection) = await Run(harness, "text", window =>
        {
            var toggle = (ToggleSwitch)window.FieldControls["legible"];
            var label = window.MeterGrid!.Children.OfType<TextBox>().First();

            // A committed edit, then the keys go to a box the page filled and nobody typed in.
            toggle.IsChecked = !fresh;
            Dispatcher.UIThread.RunJobs();
            label.Focus();
            Dispatcher.UIThread.RunJobs();
            PressIn(label, global::Avalonia.Input.Key.Z, global::Avalonia.Input.KeyModifiers.Control);
            var untouchedUndid = harness.Config.Text.KeepTextLegible == fresh;

            // The same edit again, then typing in the box: its own Ctrl+Z takes the typing back first.
            toggle.IsChecked = !fresh;
            Dispatcher.UIThread.RunJobs();
            label.Focus();
            label.CaretIndex = label.Text?.Length ?? 0;
            label.RaiseEvent(new global::Avalonia.Input.TextInputEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.TextInputEvent, Text = "x" });
            Dispatcher.UIThread.RunJobs();
            var typed = label.Text;
            PressIn(label, global::Avalonia.Input.Key.Z, global::Avalonia.Input.KeyModifiers.Control);
            var typingUndone = label.Text;
            var legibleAfterTyping = harness.Config.Text.KeepTextLegible;

            // Nothing of its own left: the next Ctrl+Z is the section's.
            PressIn(label, global::Avalonia.Input.Key.Z, global::Avalonia.Input.KeyModifiers.Control);
            return (untouchedUndid, typed, typingUndone, legibleAfterTyping, harness.Config.Text.KeepTextLegible == fresh);
        });

        await Assert.That(untouchedUndid).IsTrue().Because("a box with nothing typed in it hands Ctrl+Z to the section");
        await Assert.That(typed).EndsWith("x");
        await Assert.That(typingUndone).DoesNotEndWith("x").Because("the box's own typing is undone first");
        await Assert.That(legibleAfterTyping).IsEqualTo(!fresh).Because("that Ctrl+Z was the box's, not the section's");
        await Assert.That(thenSection).IsTrue().Because("once the box's typing is gone, Ctrl+Z is the section's again");
    }

    /// <summary>
    /// Typing committed with Enter is no longer the box's to undo: the box still has focus and the typing is still in its
    /// own history, but Ctrl+Z goes to the section, takes the committed value back and shows it in the focused box.
    /// </summary>
    [Test]
    public async Task CtrlZAfterABoxCommitsUndoesTheCommitAndShowsItInTheBox()
    {
        var harness = TwoWorlds();
        harness.History = new SettingsHistory(harness.Config, id => BuiltInSettings.All.FirstOrDefault(s => s.Id == id)?.Title);
        var (committed, prefix, shown, redone, redoneShown) = await Run(harness, "worlds", window =>
        {
            var box = (TextBox)window.FieldControls["edit-grab"];
            box.Focus();
            Dispatcher.UIThread.RunJobs();
            box.RaiseEvent(new global::Avalonia.Input.TextInputEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.TextInputEvent, Text = "Edit>" });
            PressIn(box, global::Avalonia.Input.Key.Enter, global::Avalonia.Input.KeyModifiers.None);
            var committed = harness.Config.Worlds[0].GrabPrefix;

            PressIn(box, global::Avalonia.Input.Key.Z, global::Avalonia.Input.KeyModifiers.Control);
            var prefix = harness.Config.Worlds[0].GrabPrefix;
            var shown = box.Text ?? string.Empty;

            PressIn(box, global::Avalonia.Input.Key.Z, global::Avalonia.Input.KeyModifiers.Control | global::Avalonia.Input.KeyModifiers.Shift);
            return (committed, prefix, shown, harness.Config.Worlds[0].GrabPrefix, box.Text);
        });

        await Assert.That(committed).IsEqualTo("Edit>");
        await Assert.That(prefix).IsNull().Because("Ctrl+Z after Enter undid the committed edit, not the box's typing alone");
        await Assert.That(shown).IsEqualTo(string.Empty).Because("the focused box shows the value the undo put back");
        await Assert.That(redone).IsEqualTo("Edit>").Because("Ctrl+Shift+Z is the section's redo too");
        await Assert.That(redoneShown).IsEqualTo("Edit>");
    }

    /// <summary>Raises a key on <paramref name="target"/>, so it tunnels from the window and bubbles back as a real key does.</summary>
    private static void PressIn(global::Avalonia.Interactivity.Interactive target, global::Avalonia.Input.Key key, global::Avalonia.Input.KeyModifiers modifiers)
    {
        target.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(SettingsWindow window, global::Avalonia.Input.Key key, global::Avalonia.Input.KeyModifiers modifiers)
    {
        window.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static Harness TwoWorlds(bool deferred = false)
    {
        var harness = new Harness { Deferred = deferred };
        harness.Config.Worlds.Clear();
        harness.Config.Worlds.Add(new WorldDefinition { Name = "Aetherfall", Host = "aether.example", Characters = { new CharacterDefinition { Name = "Corvid" } } });
        harness.Config.Worlds.Add(new WorldDefinition { Name = "Brightwater", Host = "bright.example", Characters = { new CharacterDefinition { Name = "Wren" } } });
        return harness;
    }

    /// <summary>
    /// F5's section is the worlds page: a tree of every world with its characters, the declared fields of the one picked,
    /// and an edit lands on that world.
    /// </summary>
    [Test]
    public async Task AWorldSectionPicksItsWorldAndEditsIt()
    {
        var harness = TwoWorlds();
        var (fields, subjects) = await Run(harness, "worlds", window =>
        {
            var tree = window.WorldTree!;
            var subjects = tree.ItemCount;
            tree.SelectedItem = tree.Items.OfType<ListBoxItem>().First(i => ReferenceEquals(i.Tag, harness.Config.Worlds[1]));
            Dispatcher.UIThread.RunJobs();
            var box = (TextBox)window.FieldControls["edit-grab"];
            box.Text = "Edit>";
            box.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter });
            Dispatcher.UIThread.RunJobs();
            return (window.ShownFields.ToArray(), subjects);
        });

        await Assert.That(subjects).IsEqualTo(4);
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

    /// <summary>
    /// A world's picture is a text field with a named Choose button beside it; a relative path is refused and put back,
    /// an https address is kept.
    /// </summary>
    [Test]
    public async Task AWorldsPictureIsCheckedAndHasAChooseButton()
    {
        var harness = TwoWorlds();
        var (refused, kept, choose) = await Run(harness, "worlds", window =>
        {
            var box = window.FieldControls["avatar"].GetVisualDescendants().OfType<TextBox>().First();
            void Enter(string text)
            {
                box.Text = text;
                box.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter });
                Dispatcher.UIThread.RunJobs();
            }

            Enter("pictures/me.png");
            var refused = box.Text;
            Enter("https://example.com/me.png");
            var choose = window.GetVisualDescendants().OfType<Button>()
                .Any(b => AutomationProperties.GetName(b) == "Choose a picture for avatar");
            return (refused, box.Text, choose);
        });

        await Assert.That(refused ?? string.Empty).IsEqualTo(string.Empty);
        await Assert.That(kept).IsEqualTo("https://example.com/me.png");
        await Assert.That(harness.Config.Worlds[0].Avatar).IsEqualTo("https://example.com/me.png");
        await Assert.That(choose).IsTrue();
    }

    /// <summary>
    /// The crop step: it opens over the page with the keyboard on the picture, Esc cancels it without closing the window,
    /// and Use hands back the square under the circle after a drag and a zoom.
    /// </summary>
    [Test]
    public async Task TheCropStepHandsBackTheSquareUnderTheCircle()
    {
        var harness = TwoWorlds();
        var (cancelled, stillOpen, focused, crop, expected, gone) = await Run(harness, "worlds", window =>
        {
            using var picture = new global::Avalonia.Media.Imaging.WriteableBitmap(
                new global::Avalonia.PixelSize(400, 200), new global::Avalonia.Vector(96, 96),
                global::Avalonia.Platform.PixelFormat.Rgba8888, global::Avalonia.Platform.AlphaFormat.Premul);

            var first = window.CropAsync(picture);
            Dispatcher.UIThread.RunJobs();
            var focused = window.CropStep!.Stage.IsFocused;
            window.CropStep.Stage.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Escape, Source = window.CropStep.Stage });
            Dispatcher.UIThread.RunJobs();
            var cancelled = first.IsCompleted && first.Result is null;
            var stillOpen = window.IsVisible;

            var second = window.CropAsync(picture);
            Dispatcher.UIThread.RunJobs();
            var stage = window.CropStep!.Stage;
            stage.ZoomBy(1);
            stage.Pan(-40, 0);
            var expected = stage.Model.Crop;
            window.CropStep.UseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            return (cancelled, stillOpen, focused, second.Result, expected, window.CropStep is null);
        });

        await Assert.That(focused).IsTrue();
        await Assert.That(cancelled).IsTrue();
        await Assert.That(stillOpen).IsTrue();
        await Assert.That(crop).IsEqualTo(expected);
        await Assert.That(crop!.Value.Side).IsEqualTo(0.5);
        await Assert.That(crop.Value.CentreX).IsGreaterThan(0.5);
        await Assert.That(gone).IsTrue();
    }

    /// <summary>
    /// Choose… opens the crop step for any picture: a large one is decoded at <see cref="SettingsWindow.CropDecodeSide"/>
    /// a side (the crop is in shares), and a file that is not a picture is refused with a reason rather than set.
    /// </summary>
    [Test]
    public async Task ALargePictureOpensForCroppingAndAFileThatIsNotOneSaysWhy()
    {
        var folder = Directory.CreateTempSubdirectory("avatar-open-");
        try
        {
            var large = Path.Combine(folder.FullName, "large.png");
            using (var bitmap = new SkiaSharp.SKBitmap(3000, 1000))
            using (var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
            using (var file = File.Create(large))
            {
                data.SaveTo(file);
            }

            var tall = Path.Combine(folder.FullName, "tall.png");
            using (var bitmap = new SkiaSharp.SKBitmap(500, 4000))
            using (var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
            using (var file = File.Create(tall))
            {
                data.SaveTo(file);
            }

            var junk = Path.Combine(folder.FullName, "junk.png");
            await File.WriteAllTextAsync(junk, "not a picture");

            var (wideProblem, wideWidth, highHeight, none, problem) = await TerminalViewTests.Session.Dispatch(() =>
            {
                var (wide, wideProblem) = SettingsWindow.OpenPicture(large);
                var (high, _) = SettingsWindow.OpenPicture(tall);
                var (none, problem) = SettingsWindow.OpenPicture(junk);
                using (wide)
                using (high)
                {
                    return (wideProblem, wide?.PixelSize.Width, high?.PixelSize.Height, none, problem);
                }
            }, CancellationToken.None);

            await Assert.That(wideProblem).IsNull();
            await Assert.That(wideWidth).IsEqualTo(SettingsWindow.CropDecodeSide);
            await Assert.That(highHeight).IsEqualTo(SettingsWindow.CropDecodeSide);

            await Assert.That(none).IsNull();
            await Assert.That(problem).Contains("not a picture");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>The window's worlds page has no door to the cell F5: everything F5 did is on the page.</summary>
    [Test]
    public async Task TheWorldsPageSendsNobodyToTheCellScreen()
    {
        var harness = TwoWorlds();
        var buttons = await Run(harness, "worlds", window =>
            window.GetVisualDescendants().OfType<Button>().Select(b => AutomationProperties.GetName(b) ?? string.Empty).ToArray());

        await Assert.That(buttons.Any(n => n.Contains("screen", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(harness.Opened).IsEmpty();
    }

    /// <summary>F9 is folded into the worlds page: it has no row of its own, and opening it shows the character.</summary>
    [Test]
    public async Task F9OpensTheWorldsPageOnTheCharacter()
    {
        var harness = TwoWorlds();
        var (rows, shown, fields) = await Run(harness, "logging", window =>
            (window.GetVisualDescendants().OfType<ListBoxItem>().Select(i => i.Tag).OfType<string>().ToArray(),
             window.WorldsShown,
             window.ShownFields.ToArray()));

        await Assert.That(rows).Contains("worlds");
        await Assert.That(rows).DoesNotContain("logging");
        await Assert.That(shown).IsSameReferenceAs(harness.Config.Worlds[0].Characters[0]);
        await Assert.That(fields).IsEquivalentTo(BuiltInSettings.Character.Fields.Select(f => f.Id).ToArray());
    }

    /// <summary>A world and a character are added from the tree's buttons, each one commit, and the page shows the new one.</summary>
    [Test]
    public async Task WorldsAndCharactersAreAddedOnThePage()
    {
        var harness = TwoWorlds();
        var shown = await Run(harness, "worlds", window =>
        {
            window.AddWorldButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.AddCharacterButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            return window.WorldsShown;
        });

        await Assert.That(harness.Config.Worlds.Count).IsEqualTo(3);
        await Assert.That(harness.Config.Worlds[2].Characters.Count).IsEqualTo(1);
        await Assert.That(shown).IsSameReferenceAs(harness.Config.Worlds[2].Characters[0]);
        await Assert.That(harness.Commits).IsEqualTo(2);
    }

    /// <summary>
    /// Every world row in the list carries its own + for a character, so adding one to a world that is not the one on show
    /// is one click, and the new character lands under that world.
    /// </summary>
    [Test]
    public async Task AWorldRowsPlusAddsACharacterToThatWorld()
    {
        var harness = TwoWorlds();
        var shown = await Run(harness, "worlds", window =>
        {
            window.AddCharacterRowButtons[harness.Config.Worlds[1]].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            return window.WorldsShown;
        });

        await Assert.That(harness.Config.Worlds[0].Characters.Count).IsEqualTo(1);
        await Assert.That(harness.Config.Worlds[1].Characters.Count).IsEqualTo(2);
        await Assert.That(shown).IsSameReferenceAs(harness.Config.Worlds[1].Characters[1]);
        await Assert.That(harness.Commits).IsEqualTo(1);
    }

    /// <summary>
    /// A world's settings fold by group: the first is open, the rest show only their heading, and a click on a heading
    /// opens it. The page is a list of sections a reader can scan rather than one long form.
    /// </summary>
    [Test]
    public async Task AWorldsSettingGroupsFoldToTheirHeadings()
    {
        var harness = TwoWorlds();
        var (before, after, group) = await Run(harness, "worlds", window =>
        {
            static bool Open(Button toggle) => ((StackPanel)toggle.Parent!).Children[1].IsVisible;
            var toggles = window.GroupToggles.Values.ToList();
            var before = toggles.Select(Open).ToArray();
            var last = toggles[^1];
            last.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var after = Open(last);
            last.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            return (before, after, window.GroupToggles.Count);
        });

        await Assert.That(group).IsGreaterThan(1);
        await Assert.That(before[0]).IsTrue();
        await Assert.That(before.Skip(1).All(open => !open)).IsTrue();
        await Assert.That(after).IsTrue();
        await Assert.That(harness.Commits).IsEqualTo(0);
    }

    /// <summary>
    /// The client commits on its own thread, so a new world or character reaches the list after the page asked to show
    /// it; the page shows it once it lands rather than falling back to the first world.
    /// </summary>
    [Test]
    public async Task ANewWorldOrCharacterIsShownOnceACommitLandsLate()
    {
        var harness = TwoWorlds(deferred: true);
        var (afterWorld, afterCharacter, afterCopy) = await Run(harness, "worlds", window =>
        {
            window.AddWorldButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var afterWorld = window.WorldsShown;
            window.AddCharacterButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var afterCharacter = window.WorldsShown;
            window.CopyCharacterButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            return (afterWorld, afterCharacter, window.WorldsShown);
        });

        var added = harness.Config.Worlds[2];
        await Assert.That(afterWorld).IsSameReferenceAs(added);
        await Assert.That(afterCharacter).IsSameReferenceAs(added.Characters[0]);
        await Assert.That(added.Characters.Count).IsEqualTo(2);
        await Assert.That(afterCopy).IsSameReferenceAs(added.Characters[1]);
    }

    /// <summary>+ Character pressed before a new world's commit lands adds to that world, never to one the page fell back to.</summary>
    [Test]
    public async Task ACharacterAddedToAWorldStillOnItsWayGoesUnderIt()
    {
        var harness = TwoWorlds(deferred: true);
        var shown = await Run(harness, "worlds", window =>
        {
            window.AddWorldButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.AddCharacterButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            return window.WorldsShown;
        });

        await Assert.That(harness.Config.Worlds[0].Characters.Count).IsEqualTo(1);
        await Assert.That(harness.Config.Worlds[1].Characters.Count).IsEqualTo(1);
        await Assert.That(harness.Config.Worlds[2].Characters.Count).IsEqualTo(1);
        await Assert.That(shown).IsSameReferenceAs(harness.Config.Worlds[2].Characters[0]);
    }

    /// <summary>Delete asks first: nothing goes until the yes, and the yes deletes the world shown.</summary>
    [Test]
    public async Task DeletingAWorldAsksFirst()
    {
        var harness = TwoWorlds();
        var (before, asked) = await Run(harness, "worlds", window =>
        {
            window.DeleteButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var before = harness.Config.Worlds.Count;
            var asked = window.ConfirmDeleteButton is not null;
            window.ConfirmDeleteButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            return (before, asked);
        });

        await Assert.That(before).IsEqualTo(2);
        await Assert.That(asked).IsTrue();
        await Assert.That(harness.Config.Worlds.Select(w => w.Name)).IsEquivalentTo(new[] { "Brightwater" });
    }

    /// <summary>A character's page lists every set, and ticking one lists it on the character in one commit.</summary>
    [Test]
    public async Task ACharactersSetsAreTickedOnItsPage()
    {
        var harness = TwoWorlds();
        harness.Config.TriggerSets.Add(new TriggerSet { Name = "Combat" });
        var boxes = await Run(harness, "logging", window =>
        {
            var names = window.SetBoxes.Keys.ToArray();
            window.SetBoxes["Combat"].IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            return names;
        });

        await Assert.That(boxes).Contains("Combat");
        await Assert.That(harness.Config.Worlds[0].Characters[0].TriggerSets).Contains("Combat");
        await Assert.That(harness.Commits).IsEqualTo(1);
    }

    /// <summary>A character's connect button asks the client for that session; the page never dials itself.</summary>
    [Test]
    public async Task TheConnectButtonAsksTheClient()
    {
        var harness = TwoWorlds();
        await Run(harness, "logging", window =>
        {
            window.ConnectButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            return 0;
        });

        await Assert.That(harness.Connects).IsEquivalentTo(new[] { "Aetherfall.Corvid" });
        await Assert.That(harness.Commits).IsEqualTo(0);
    }

    /// <summary>With no world there is nothing to edit: no fields, and the ways to get one (add, browse, import).</summary>
    [Test]
    public async Task AWorldSectionWithNoWorldsOffersWaysToGetOne()
    {
        var harness = new Harness();
        harness.Config.Worlds.Clear();
        var (fields, add, buttons) = await Run(harness, "worlds", window =>
            (window.ShownFields.Count, window.AddWorldButton is not null,
             window.GetVisualDescendants().OfType<Button>().Select(b => AutomationProperties.GetName(b) ?? string.Empty).ToArray()));

        await Assert.That(fields).IsEqualTo(0);
        await Assert.That(add).IsTrue();
        await Assert.That(buttons.Any(n => n.Contains("Browse", StringComparison.Ordinal))).IsTrue();
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
