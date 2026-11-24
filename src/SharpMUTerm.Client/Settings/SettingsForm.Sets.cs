using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Settings;

/// <summary>
/// The trigger sets themselves, as the list pages (F2, F3, F4, F6) manage them: make one, name it, describe it, choose
/// who uses it, delete it. Every change goes through the funnel like any other edit, with a name for the undo history;
/// the operations are <see cref="TriggerSetEditing"/>'s, which the cell sets screen runs too.
/// </summary>
public sealed partial class SettingsForm
{
    /// <summary>The character a set made here is used by from the start: the one the form was opened for.</summary>
    public CharacterDefinition? SetOwner => _character;

    /// <summary>Every character, with whether it uses <paramref name="set"/>.</summary>
    public IReadOnlyList<SetUser> UsersOf(TriggerSet set) => TriggerSetEditing.Users(_config, set);

    /// <summary>Who uses <paramref name="set"/>, as its heading says it (<c>used by Corvid</c>).</summary>
    public string UsageOf(TriggerSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return TriggerSetEditing.Usage(_config.Worlds, set.Name);
    }

    /// <summary>Whether any character uses <paramref name="set"/>.</summary>
    public bool IsUsed(TriggerSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return TriggerSetEditing.IsUsed(_config.Worlds, set.Name);
    }

    /// <summary>Whether <paramref name="set"/> is one of the player's own sets (not a package's), so it can be changed.</summary>
    public bool IsOwnSet(TriggerSet set) => _config.TriggerSets.Contains(set);

    /// <summary>
    /// Makes a new empty set through the funnel, used by the character the form was opened for, and selects nothing in
    /// it (it has no items yet). Returns the set, which is in the configuration once the commit lands.
    /// </summary>
    public TriggerSet NewSet() => NewSet(_character);

    /// <summary>Makes a new empty set through the funnel, used by <paramref name="usedBy"/> when one is given.</summary>
    public TriggerSet NewSet(CharacterDefinition? usedBy)
    {
        var owner = usedBy;
        var set = new TriggerSet
        {
            Name = WorldEditing.Available(_config.TriggerSets.ToArray().Select(s => s.Name), TriggerSetEditing.NewSetName),
        };
        Commit(
            () =>
            {
                // Named again as it lands: two sets made before either commit would otherwise share a name.
                set.Name = WorldEditing.Available(_config.TriggerSets.ToArray().Select(s => s.Name), TriggerSetEditing.NewSetName);
                _config.TriggerSets = [.. _config.TriggerSets, set];
                if (owner is not null)
                {
                    TriggerSetEditing.Use(owner, set.Name, on: true);
                }
            },
            "making a set");
        return set;
    }

    /// <summary>Renames <paramref name="set"/> and every reference to it; the reason when the name is refused, else null.</summary>
    public string? RenameSet(TriggerSet set, string name)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (!IsOwnSet(set))
        {
            return ReadOnlyRefusal;
        }

        if (TriggerSetEditing.NameProblem(_config, set, name) is { } problem)
        {
            return problem;
        }

        if (string.Equals(set.Name, name.Trim(), StringComparison.Ordinal))
        {
            return null;
        }

        Commit(() => TriggerSetEditing.Rename(_config, set, name), "renaming a set");
        return null;
    }

    /// <summary>Sets <paramref name="set"/>'s description (blank clears it); false for a package's set.</summary>
    public bool DescribeSet(TriggerSet set, string? description)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (!IsOwnSet(set))
        {
            return false;
        }

        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (string.Equals(set.Description, text, StringComparison.Ordinal))
        {
            return true;
        }

        Commit(() => set.Description = text, "describing a set");
        return true;
    }

    /// <summary>Deletes <paramref name="set"/> and every character's use of it; false for a package's set.</summary>
    public bool DeleteSet(TriggerSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (!IsOwnSet(set))
        {
            return false;
        }

        if (_item is not null && Sections.Any(s => s.Collection?.ItemsIn(set).Any(i => ReferenceEquals(i, _item)) == true))
        {
            _item = null;
        }

        Commit(() => TriggerSetEditing.Delete(_config, set), "deleting a set");
        return true;
    }

    /// <summary>What deleting <paramref name="set"/> costs, in words, for the question asked first.</summary>
    public string SetDeletion(TriggerSet set) => TriggerSetEditing.Deletion(_config.Worlds, set);
}
