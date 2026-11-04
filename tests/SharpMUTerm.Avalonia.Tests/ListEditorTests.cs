using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Packages;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The settings window's list sections (F2, F3, F4, F6): a master-detail editor whose detail is one control per
/// declared item field, whose buttons add, duplicate, delete and reorder through the form's one commit, which
/// refuses a package's set and offers to copy it, and whose F2 page runs the live test over recent output.
/// </summary>
[NotInParallel]
public class ListEditorTests
{
    private sealed class Harness
    {
        public Harness()
        {
            Config.TriggerSets.Clear();
            Config.TriggerSets.Add(Own);
            Config.Packages = [new InstalledPackage(new PackageManifest { Id = "pack", Name = "Pack", Version = "1.0.0" }, [Package])];
        }

        public AppConfiguration Config { get; } = new();

        public TriggerSet Own { get; } = new()
        {
            Name = "public",
            Triggers =
            {
                new Trigger { Name = "xp", Pattern = "You gain * experience", MatchType = TriggerMatchType.Wildcard },
                new Trigger { Name = "tell", Pattern = "tells you" },
            },
        };

        public TriggerSet Package { get; } = new() { Name = "combat", Triggers = { new Trigger { Name = "theirs", Pattern = "x" } } };

        public int Commits { get; set; }

        public SettingsForm Form(string initial, object? item = null)
        {
            SettingsForm? form = null;
            var context = new SettingsContext(
                ["Chat"],
                ["You gain 40 experience points.", "A guard nods.", "You gain 7 experience points."],
                _ => null,
                ["F10", "F11", "F12"]);
            form = new SettingsForm(
                BuiltInSettings.All,
                Config,
                change =>
                {
                    change();
                    Commits++;
                    form!.NotifyChanged();
                },
                _ => { },
                initial,
                isDark: true,
                context: context,
                item: item);
            return form;
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Test]
    public async Task EachListPageDrawsOneControlPerDeclaredItemField()
    {
        foreach (var section in new[] { BuiltInSettings.Triggers, BuiltInSettings.Aliases, BuiltInSettings.Timers })
        {
            var harness = new Harness();
            harness.Own.Aliases.Add(new Alias { Name = "a", Pattern = "^a$", Substitution = "look" });
            harness.Own.Timers.Add(new TimerDefinition { Name = "t", Command = "look" });
            var (shown, controls) = await Run(harness, section.Id, window =>
                (window.ShownFields.ToArray(), window.FieldControls.Keys.ToArray()));

            var declared = section.Collection!.Fields.Select(f => f.Id).ToArray();
            await Assert.That(shown).IsEquivalentTo(declared);
            await Assert.That(controls.Except([section.Collection.EnabledField])).IsEquivalentTo(declared.Except([section.Collection.EnabledField]));
        }
    }

    [Test]
    public async Task TheButtonsAddDuplicateMoveAndDeleteThroughTheCommit()
    {
        var harness = new Harness();
        var own = harness.Own;
        var xp = own.Triggers[0];
        var (afterAdd, afterDuplicate, afterMove, afterDelete) = await Run(harness, "triggers", window =>
        {
            var editor = window.ListEditor!;
            editor.Choose(xp);

            Click(editor.AddButton);
            Dispatcher.UIThread.RunJobs();
            var added = own.Triggers.Count;

            editor.Choose(xp);
            Click(editor.DuplicateButton);
            Dispatcher.UIThread.RunJobs();
            var duplicated = own.Triggers.Select(t => t.Name).ToArray();

            editor.Choose(xp);
            Click(editor.DownButton);
            Dispatcher.UIThread.RunJobs();
            var moved = own.Triggers.IndexOf(xp);

            Click(editor.DeleteButton);
            Dispatcher.UIThread.RunJobs();
            return (added, duplicated, moved, own.Triggers.Contains(xp));
        });

        await Assert.That(afterAdd).IsEqualTo(3);
        await Assert.That(afterDuplicate.Take(2)).IsEquivalentTo(new[] { "xp", "xp copy" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(afterMove).IsEqualTo(1);
        await Assert.That(afterDelete).IsFalse();
        await Assert.That(harness.Commits).IsEqualTo(4);
    }

    [Test]
    public async Task APackagesRuleIsReadOnlyAndItsSetCanBeCopiedToEdit()
    {
        var harness = new Harness();
        var theirs = harness.Package.Triggers[0];
        var (enabledControls, rowEnabled, copies) = await Run(harness, "triggers", window =>
        {
            var editor = window.ListEditor!;
            editor.Choose(theirs);
            var controls = editor.FieldControls.Values.Count(c => c.IsEnabled);
            var row = editor.Rows.Single(r => ReferenceEquals(r.Item, theirs)).Enabled.IsEnabled;
            Click(editor.DeleteButton);
            Dispatcher.UIThread.RunJobs();
            var buttons = editor.CopyButtons.Count;
            Click(editor.CopyButtons.Single());
            Dispatcher.UIThread.RunJobs();
            return (controls, row, buttons);
        });

        await Assert.That(enabledControls).IsEqualTo(0);
        await Assert.That(rowEnabled).IsFalse();
        await Assert.That(copies).IsEqualTo(1);
        await Assert.That(harness.Package.Triggers.Single()).IsSameReferenceAs(theirs).Because("nothing deleted it");
        await Assert.That(harness.Config.TriggerSets.Count).IsEqualTo(2);
        await Assert.That(harness.Config.TriggerSets[1].Triggers.Single().Name).IsEqualTo("theirs");
        await Assert.That(harness.Commits).IsEqualTo(1);
    }

    [Test]
    public async Task TheLiveTestListsTheMatchingLinesWithTheMatchMarked()
    {
        var harness = new Harness();
        var (lines, marked) = await Run(harness, "triggers", window =>
        {
            var editor = window.ListEditor!;
            editor.Choose(harness.Own.Triggers[0]);
            var blocks = editor.PreviewLines;
            var bold = blocks.Skip(1)
                .Select(b => b.Inlines?.OfType<Run>().FirstOrDefault(r => r.FontWeight == global::Avalonia.Media.FontWeight.Bold)?.Text)
                .ToArray();
            return (blocks.Select(b => b.Text ?? string.Concat(b.Inlines!.OfType<Run>().Select(r => r.Text))).ToArray(), bold);
        });

        await Assert.That(lines[0]).Contains("matches 2 of the last 3");
        await Assert.That(lines.Skip(1).Count()).IsEqualTo(2);
        await Assert.That(marked).All().Satisfy(m => m.StartsWith("You gain", StringComparison.Ordinal), m => m.IsTrue());
    }

    [Test]
    public async Task AFormOpenedOnARuleSelectsIt()
    {
        var harness = new Harness();
        var tell = harness.Own.Triggers[1];
        var (selected, row) = await Run(harness, "triggers", window =>
            (window.ListEditor!.SelectedItem, (window.ListEditor.List.SelectedItem as ListBoxItem)?.Tag), tell);

        await Assert.That(selected).IsSameReferenceAs(tell);
        await Assert.That(row).IsSameReferenceAs(tell);
    }

    [Test]
    public async Task AListKeepsItsSelectionAcrossASectionSwitchAndAReopen()
    {
        var harness = new Harness();
        harness.Config.TriggerSets[0].Aliases.Add(new Alias { Name = "n", Pattern = "n" });
        var kept = new Dictionary<string, object>(StringComparer.Ordinal);
        var tell = harness.Own.Triggers[1];
        var (afterSwitch, afterReopen) = await TerminalViewTests.Session.Dispatch(() =>
        {
            static void Pick(SettingsWindow window, string id)
            {
                window.SectionList.SelectedItem = window.SectionList.Items.OfType<ListBoxItem>().First(i => Equals(i.Tag, id));
                Dispatcher.UIThread.RunJobs();
            }

            var form = new SettingsForm(BuiltInSettings.All, harness.Config, c => c(), _ => { }, "triggers", isDark: true) { Selections = kept };
            var window = new SettingsWindow(form);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.ListEditor!.Choose(tell);
            Pick(window, "aliases");
            Pick(window, "triggers");
            var switched = window.ListEditor!.SelectedItem;
            window.Close();

            // A new window, on a new form, as F2 opens one: the run's memory brings it back to the same rule.
            var again = new SettingsWindow(new SettingsForm(BuiltInSettings.All, harness.Config, c => c(), _ => { }, "triggers", isDark: true) { Selections = kept });
            again.Show();
            Dispatcher.UIThread.RunJobs();
            var reopened = again.ListEditor!.SelectedItem;
            again.Close();
            return (switched, reopened);
        }, CancellationToken.None);

        await Assert.That(afterSwitch).IsSameReferenceAs(tell);
        await Assert.That(afterReopen).IsSameReferenceAs(tell);
    }

    [Test]
    public async Task TheSearchFindsAnItemFieldAndShowsOnlyIt()
    {
        var harness = new Harness();
        var shown = await Run(harness, "text", window =>
        {
            window.SearchBox.Text = "gag line";
            Dispatcher.UIThread.RunJobs();
            return (window.ShownSection, window.ShownFields.ToArray());
        });

        await Assert.That(shown.ShownSection).IsEqualTo("triggers");
        await Assert.That(shown.Item2).Contains("gag");
        await Assert.That(shown.Item2).DoesNotContain("pattern");
    }

    private static Task<T> Run<T>(Harness harness, string initial, Func<SettingsWindow, T> body, object? item = null) =>
        TerminalViewTests.Session.Dispatch(() =>
        {
            var window = new SettingsWindow(harness.Form(initial, item));
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
}
