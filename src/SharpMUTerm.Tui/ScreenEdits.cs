using SharpMUTerm.Client.Settings;

namespace SharpMUTerm.Tui;

/// <summary>
/// The one path from a settings screen into the configuration, and the point at which each change is
/// written to disk. Screens edit the live
/// <see cref="SharpMUTerm.Core.Configuration.AppConfiguration"/> in place — the live object is what the
/// running client reads, so an edit applied to a copy would be an edit the app does not have — making
/// this a gateway rather than a transaction.
/// <para>
/// <b>The scope rule, which is what makes the three answers to Esc obviously right.</b> Esc discards
/// work that has not been confirmed, keeps work that has, and <em>asks</em> about work that destroyed
/// something:
/// </para>
/// <list type="number">
/// <item>
/// An <b>uncommitted field buffer</b> never reaches this class at all. What is being typed lives in
/// <see cref="SettingsSession"/> until ⏎ accepts it; Esc drops the buffer, and config never saw it.
/// </item>
/// <item>
/// A <b>committed</b> field, a flipped checkbox and an added row are <b>kept unconditionally</b>. They
/// are applied the moment they are made and persisted immediately, so leaving the screen is navigation
/// and not a transaction boundary. This is the fix for "I changed the address and it went back": Esc
/// used to replay an undo log over everything, so the key the header called <c>close</c> silently threw
/// away every edit on the screen.
/// </item>
/// <item>
/// A <b>deletion</b> is recorded here, because it is the one change whose subject is gone and cannot be
/// retyped. Closing the screen with deletions pending asks once, on the way out, naming each of them
/// (<see cref="ScreenEditReview"/>); <see cref="Revert"/> is what "no, put them back" runs. It is a
/// batch review rather than a confirmation per press, so deleting three characters interrogates you
/// once instead of three times.
/// </item>
/// </list>
/// <para>
/// Every structural deletion is logged, not only the cascading ones (a world and its characters, a
/// trigger set and its rules): the mechanism is identical, and a trigger set holding fifteen rules is
/// not meaningfully less destructive than a character. To narrow it to a subset, stop the button
/// handing back a <see cref="ScreenPress.Undo"/> — that is the single switch, and a press with no undo
/// is simply kept.
/// </para>
/// <para>
/// Pure state apart from the persist callback, so the semantics are unit-testable without a window.
/// </para>
/// </summary>
internal sealed class ScreenEdits
{
    private readonly List<Deletion> _deletions = new();
    private readonly Action? _persist;
    private SettingsHistory? _history;
    private string? _section;

    /// <summary>
    /// <paramref name="persist"/> writes the configuration out, and is run after <em>every</em> accepted
    /// change rather than on the way out of the screen.
    /// <para>
    /// That choice is the other half of the "it does not stick" fix. Under apply-immediately semantics a
    /// committed value is already real — the live config object is what the client reads — so the only
    /// question is whether the file agrees, and deferring the write to some later gesture is how "it
    /// stuck until I restarted" happens. Writing per change also means a crash cannot eat one.
    /// <c>config.json</c> is a few kilobytes and a keystroke that commits is rare (⏎, Space, a button),
    /// so the cost is nothing; the alternative — write on close — buys only the ability to have unsaved
    /// state, which is precisely what the user asked us to stop having.
    /// </para>
    /// <para>
    /// A deletion is persisted like anything else, and <see cref="Revert"/> persists the restoration. The
    /// review is a review, not a staging area.
    /// </para>
    /// </summary>
    internal ScreenEdits(Action? persist = null) => _persist = persist;

    /// <summary>Whether any deletion is waiting to be reviewed on the way out of the screen.</summary>
    internal bool HasDeletions => _deletions.Count > 0;

    /// <summary>
    /// What has been deleted, oldest first, in the words the review names them by — <c>Aetherfall and
    /// its 2 characters</c>, not <c>1 deletion</c>. Same discipline as the quit prompt naming the worlds
    /// it would disconnect: a question that says what it is about is answerable, and "are you sure?" is
    /// a ritual.
    /// </summary>
    internal IReadOnlyList<string> Deletions => _deletions.ConvertAll(d => d.What);

    /// <summary>
    /// Records every change from now on in <paramref name="history"/> as an edit of <paramref name="section"/>, so ⌃Z on
    /// that section can put it back (<see cref="Undo"/>). The app does this for each screen it opens and for the native
    /// settings window; a screen with no history (the tests that exercise only the funnel) records nothing.
    /// </summary>
    internal void Track(SettingsHistory? history, string? section)
    {
        _history = section is null ? null : history;
        _section = section;
    }

    /// <summary>Undoes the last edit recorded for this screen's section, saves the result, and says what it did.</summary>
    internal SettingsUndoResult Undo() => Step(redo: false);

    /// <summary>Redoes the last edit undone on this screen's section, saves the result, and says what it did.</summary>
    internal SettingsUndoResult Redo() => Step(redo: true);

    private SettingsUndoResult Step(bool redo)
    {
        if (_history is not { } history || _section is not { } section)
        {
            return new SettingsUndoResult(false, redo ? "Nothing to redo." : "Nothing to undo.");
        }

        var result = redo ? history.Redo(section) : history.Undo(section);
        if (!result.Done)
        {
            return result;
        }

        // A deletion undone is no longer one for the closing review to put back (it would be put back twice); one
        // redone is again.
        if (result.Tag is Deletion deletion)
        {
            if (redo)
            {
                if (!_deletions.Contains(deletion))
                {
                    _deletions.Add(deletion);
                }
            }
            else
            {
                _deletions.Remove(deletion);
            }
        }

        _persist?.Invoke();
        return result;
    }

    /// <summary>Runs <paramref name="change"/>, saves it, and records it in the history under <paramref name="what"/>.</summary>
    private T Recorded<T>(string? what, Func<T> change, Func<T, object?>? tag = null, Func<T, bool>? changed = null)
    {
        var before = _history?.Capture();
        var result = change();
        if (changed?.Invoke(result) == false)
        {
            return result;
        }

        _persist?.Invoke();
        if (before is not null)
        {
            _history!.Record(_section!, before, what, tag?.Invoke(result));
        }

        return result;
    }

    /// <summary>Flips a checkbox and writes it out. Committed on the spot; nothing to review.</summary>
    /// <summary>
    /// Puts a declared setting back to its default — the same funnel as every other change, so a reset is
    /// saved, and reaches a connected session, exactly as typing the default in would.
    /// </summary>
    internal void Apply(Action reset, string? what = null)
    {
        ArgumentNullException.ThrowIfNull(reset);
        Recorded(what, () =>
        {
            reset();
            return 0;
        });
    }

    /// <summary>
    /// Moves something (an item within its set) and saves it only if it moved: <paramref name="move"/> answers where it
    /// went, or null when it was already at that end and nothing changed.
    /// </summary>
    internal int? Apply(Func<int?> move, string? what = null)
    {
        ArgumentNullException.ThrowIfNull(move);
        return Recorded(what, move, changed: row => row is not null);
    }

    internal void Apply(ScreenToggle toggle, string? what = null) => Recorded(what, () =>
    {
        toggle.Flip();
        return 0;
    });

    /// <summary>
    /// Writes a typed value to a field and persists it. Nothing is written when the value doesn't
    /// validate: returns null when the value was accepted, otherwise why it was refused. This is the
    /// only path from a buffer into config, which is what keeps an invalid value out of it — and what
    /// makes "Esc abandons the buffer" a true statement rather than a hopeful one.
    /// </summary>
    internal string? Apply(ScreenField field, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (field.Validate(value) is { } error)
        {
            return error;
        }

        Recorded(field.Label, () =>
        {
            field.Set(value);
            return 0;
        });
        return null;
    }

    /// <summary>
    /// Runs a button and persists the result. A press that hands back an undo destroyed something, so it
    /// is recorded for the closing review, described by what it took with it; a press that hands back
    /// none built something and is simply kept. Returns where the button wants the cursor left, or null
    /// when it doesn't care.
    /// <para>
    /// The description is taken <em>before</em> the press, because it is the only moment the answer can
    /// still be counted — a world's characters are gone by the time <see cref="ScreenButton.Run"/>
    /// returns. The undo, conversely, can only be known afterwards: see <see cref="ScreenButton"/>.
    /// Deletions replay newest-first, because an index captured by a removal is only meaningful against
    /// the list as it stood at that moment.
    /// </para>
    /// </summary>
    internal int? Apply(ScreenButton button)
    {
        var what = button.Describe?.Invoke() ?? button.Target;
        Deletion? deletion = null;
        var press = Recorded(
            button.Kind == ScreenButtonKind.Remove ? $"deleting {what ?? button.Label}" : button.Label.Trim('[', ']', ' '),
            () =>
            {
                var press = button.Run();
                if (press.Undo is { } undo)
                {
                    deletion = new Deletion(what ?? button.Label, undo);
                    _deletions.Add(deletion);
                }

                return press;
            },
            _ => deletion);
        return press.Select;
    }

    /// <summary>
    /// Puts every logged deletion back, newest first, and writes the result out. The answer to "no, keep
    /// them" is <see cref="Commit"/>; this is the answer to "no, put them back".
    /// </summary>
    internal void Revert()
    {
        for (var i = _deletions.Count - 1; i >= 0; i--)
        {
            _deletions[i].Undo();
        }

        _deletions.Clear();
        _persist?.Invoke();
    }

    /// <summary>Accepts the logged deletions; there is nothing left to review.</summary>
    internal void Commit() => _deletions.Clear();

    /// <summary>A deletion waiting for the closing review: what it took, and how to put it back.</summary>
    private sealed class Deletion(string what, Action undo)
    {
        internal string What { get; } = what;

        internal Action Undo { get; } = undo;
    }
}
