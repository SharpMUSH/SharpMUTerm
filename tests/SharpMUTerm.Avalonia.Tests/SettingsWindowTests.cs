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

        public SettingsForm Form(string initial = "text")
        {
            SettingsForm? form = null;
            form = new SettingsForm(
                BuiltInSettings.All,
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
                meter => meter.Value == "Char.Vitals.hp" ? "412/500" : null);
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
        await Assert.That(badges["input"]).IsEqualTo("0");
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

            var add = window.GetVisualDescendantsOf<Button>().Single(b => Equals(b.Content, "+ Meter"));
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
        var harness = new Harness();
        var shown = await Run(harness, "text", window =>
        {
            var item = window.SectionList.Items.OfType<ListBoxItem>().Single(i => Equals(i.Tag, "triggers"));
            window.SectionList.SelectedItem = item;
            Dispatcher.UIThread.RunJobs();
            return window.ShownSection;
        });

        await Assert.That(harness.Opened).IsEquivalentTo(new[] { "triggers" });
        await Assert.That(shown).IsEqualTo("text");
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
