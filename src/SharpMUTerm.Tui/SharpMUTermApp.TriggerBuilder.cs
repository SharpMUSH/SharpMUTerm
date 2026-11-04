using SharpConsoleUI.Controls;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Diagnostics;

namespace SharpMUTerm.Tui;

/// <summary>
/// The trigger builder: ⌃P ▸ <c>Make a trigger from the selection</c> turns selected output into a new rule
/// and opens F2 on it with the pattern being typed, and F2's live test runs the pattern over what the panes
/// hold on every keystroke.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>What the set a rule is made in is called when the configuration has none to put it in.</summary>
    internal const string BuilderSetName = "My triggers";

    /// <summary>
    /// The output a pane has selected, and which window it is in — or null when what is selected is not a
    /// pane's (the command line) or nothing is. Asked of the window's <c>SelectionManager</c>, for
    /// <see cref="CopySelection"/>'s reason: it owns the one selection, wherever the focus is.
    /// </summary>
    private (string Text, string? WindowId)? SelectedOutput()
    {
        if (_window.SelectionManager.ActiveSelection is not MarkupControl { HasSelection: true } pane)
        {
            return null;
        }

        var text = SelectedText.Of(pane);
        if (text.Trim().Length == 0)
        {
            return null;
        }

        var windowId = _panes.Concat(_frozenPanes)
            .FirstOrDefault(entry => ReferenceEquals(entry.Value, pane)).Key;
        return (text, windowId);
    }

    /// <summary>Whether some pane's output is selected — what the ⌃P entry is offered on.</summary>
    private bool HasOutputSelection =>
        _window.SelectionManager.ActiveSelection is MarkupControl { HasSelection: true };

    /// <summary>
    /// ⌃P ▸ <c>Make a trigger from the selection</c>. The rule is made at once — enabled, with no actions,
    /// so it does nothing to the stream until it is given something to do — and F2 opens on it with the
    /// pattern field open, so the next keystroke edits the suggestion and the live test answers it.
    /// <para>
    /// It goes in the first editable set of the character whose window the selection is in: the
    /// character's own first listed set, else the first set F2 lists, else a new one. A package's sets are
    /// never written to (they are replaced wholesale by an update), and they are not on F2 anyway.
    /// </para>
    /// </summary>
    private bool MakeTriggerFromSelection()
    {
        if (SelectedOutput() is not { } selected)
        {
            RefuseCommand("nothing selected — drag across a pane's output to select the line the trigger is for");
            return false;
        }

        if (_settings.IsOpen)
        {
            RefuseCommand("close the settings screen first — the trigger opens on F2");
            return false;
        }

        if (ComposerIsInTheWay("the trigger editor"))
        {
            return false;
        }

        var windowId = selected.WindowId ?? ActiveWindowId();
        var session = WindowSession(windowId);
        var suggestion = TriggerSuggestion.Suggest(selected.Text, session?.Completions.Names());
        var set = BuilderSet(session?.SessionKey ?? _workspace.FindWindow(windowId)?.SessionKey);

        var trigger = new Trigger
        {
            Name = ScreenLists.Available(_config.TriggerSets.SelectMany(s => s.Triggers).Select(t => t.Name), suggestion.Name),
            Pattern = suggestion.Pattern,
            MatchType = suggestion.MatchType,
        };
        set.Triggers.Add(trigger);
        SaveConfiguration();

        // The declared editor in either host: the window's native list on the new rule, or the cell F2 with its pattern
        // being typed.
        if (TryOpenNativeSettings(Section("triggers"), trigger))
        {
            return true;
        }

        var index = _config.TriggerSets.SelectMany(s => s.Triggers).ToList().IndexOf(trigger);
        _settings.Toggle(ConsoleKey.F2, () => TriggersScreen(editPattern: index));
        return true;
    }

    /// <summary>
    /// The set a rule made from a selection goes in, for the character <paramref name="owner"/> names
    /// (<c>world.character</c>), creating one when the configuration has no sets at all — and listing a
    /// created set on that character, or the rule would sit in a set nobody runs.
    /// </summary>
    private TriggerSet BuilderSet(string? owner)
    {
        var character = owner is null ? null : CharacterFor(owner);
        if (character is not null)
        {
            foreach (var name in character.TriggerSets)
            {
                if (_config.TriggerSets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal)) is { } own)
                {
                    return own;
                }
            }
        }

        if (_config.TriggerSets.FirstOrDefault() is { } first)
        {
            return first;
        }

        var created = new TriggerSet { Name = BuilderSetName };
        _config.TriggerSets.Add(created);
        character?.TriggerSets.Add(created.Name);
        Notice($"made a trigger set called {BuilderSetName} for it", MessageSeverity.Info);
        return created;
    }

    /// <summary>The lines the <c>trigger-builder</c> view lands in the main window before selecting one.</summary>
    internal static IReadOnlyList<string> TriggerBuilderScene { get; } =
    [
        "You gain 125 experience points.",
        "Rivane says, \"to the crypt, then!\"",
        "A town guard nods to you.",
        "You gain 40 experience points.",
    ];

    /// <summary>
    /// The <c>trigger-builder</c> view: <see cref="TriggerBuilderScene"/> lands in the main window, the last
    /// line is selected by a real drag (<see cref="SimulatePaneDrag"/>), and the real ⌃P entry is dispatched.
    /// The row is measured, not counted, because the demo's room description wraps in a narrow pane.
    /// </summary>
    private void StageTriggerBuilder()
    {
        foreach (var line in TriggerBuilderScene)
        {
            AppendWindowLine(MainWindowId, MarkupText.Escape(line));
        }

        RenderWholeFrame();
        var buffer = _lines[MainWindowId];
        var target = buffer.Count - 1;
        var width = _paneScrolls.GetValueOrDefault(MainWindowId)?.ViewportWidth ?? 0;
        var row = MeasureRows(buffer, 0, width, _panes.GetValueOrDefault(MainWindowId), target);
        SimulatePaneDrag(MainWindowId, 0, row, buffer[target].Plain.Length, row);
        ReArmWholeFrame();
        DispatchCommand("term:trigger-from-selection");
    }

    /// <summary>
    /// What F2's live test runs over: the plain text of every window a pane holds, ⌃F's corpus
    /// (<see cref="SearchableWindows"/>), with the focused window's lines last so they count as the newest —
    /// they are the ones the rule is most likely being written about. Chrome rows carry no plain text and
    /// are left out, so a test cannot match the client's own bars. <see cref="TriggerPreview"/> takes the
    /// newest <see cref="TriggerPreview.MaxLines"/> of it.
    /// </summary>
    internal IReadOnlyList<string> TriggerTestLines()
    {
        var focused = ActiveWindowId();
        var windows = SearchableWindows(all: true);
        return windows.Where(w => !string.Equals(w.WindowId, focused, StringComparison.Ordinal))
            .Concat(windows.Where(w => string.Equals(w.WindowId, focused, StringComparison.Ordinal)))
            .SelectMany(w => w.Lines)
            .Where(line => line.Length > 0)
            .ToList();
    }
}
