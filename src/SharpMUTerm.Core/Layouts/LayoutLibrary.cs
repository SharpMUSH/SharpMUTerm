namespace SharpMUTerm.Core.Layouts;

/// <summary>What <see cref="LayoutLibrary.Update"/> did: changed the layout in place, or saved a copy.</summary>
/// <param name="Layout">The layout that now holds the placements.</param>
/// <param name="CreatedNew">True when the named layout was a built-in and a new one was saved instead.</param>
public sealed record LayoutSaveResult(LayoutDefinition Layout, bool CreatedNew);

/// <summary>
/// The layouts a player can pick from: <see cref="BuiltInLayouts"/> first, then their own, which live in the list
/// handed in (<c>AppConfiguration.Layouts</c>). Every change is made to that list, so saving the configuration
/// is what persists it; this type writes no file.
/// <para>
/// A built-in is never changed. <see cref="Update"/> on one saves a copy under a fresh name, and
/// <see cref="Rename"/> and <see cref="Delete"/> refuse. Names are unique across the whole library, ignoring case,
/// so a picker never shows two rows that read the same.
/// </para>
/// </summary>
public sealed class LayoutLibrary
{
    private readonly List<LayoutDefinition> _user;
    private readonly Func<string> _newId;

    /// <param name="userLayouts">The player's layouts, changed in place.</param>
    /// <param name="newId">Makes the id of a new layout; a GUID by default. A test passes its own.</param>
    public LayoutLibrary(List<LayoutDefinition> userLayouts, Func<string>? newId = null)
    {
        _user = userLayouts ?? throw new ArgumentNullException(nameof(userLayouts));
        _newId = newId ?? (() => Guid.NewGuid().ToString("N"));
    }

    /// <summary>The built-ins, then the player's layouts in the order they were saved.</summary>
    public IReadOnlyList<LayoutDefinition> All => [.. BuiltInLayouts.All, .. _user];

    /// <summary>
    /// The layout with <paramref name="id"/>, or null. A built-in wins over a saved layout that happens to carry
    /// the same id, so a hand-edited file cannot shadow one.
    /// </summary>
    public LayoutDefinition? Find(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return BuiltInLayouts.Find(id)
            ?? _user.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Whether some layout other than <paramref name="exceptId"/> is already called <paramref name="name"/>.</summary>
    public bool IsNameTaken(string name, string? exceptId = null) =>
        All.Any(l => !string.Equals(l.Id, exceptId, StringComparison.Ordinal)
            && string.Equals(l.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <paramref name="name"/> if no layout has it, otherwise the first of <c>name 2</c>, <c>name 3</c>… that is
    /// free. A blank name becomes <c>Layout</c>.
    /// </summary>
    public string UniqueName(string? name)
    {
        var stem = string.IsNullOrWhiteSpace(name) ? "Layout" : name.Trim();
        if (!IsNameTaken(stem))
        {
            return stem;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} {n}";
            if (!IsNameTaken(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Saves <paramref name="placements"/> as a new layout. The name is made unique (<see cref="UniqueName"/>)
    /// rather than refused, so "save current" always succeeds.
    /// </summary>
    public LayoutDefinition SaveAsNew(
        string? name,
        IEnumerable<PanelPlacement> placements,
        IReadOnlyDictionary<LayoutRegion, double>? regionShares = null)
    {
        ArgumentNullException.ThrowIfNull(placements);
        var layout = new LayoutDefinition
        {
            Id = _newId(),
            Name = UniqueName(name),
            Placements = placements.ToArray(),
            RegionShares = regionShares is null ? null : new Dictionary<LayoutRegion, double>(regionShares),
        };
        _user.Add(layout);
        return layout;
    }

    /// <summary>
    /// Replaces the placements of the layout with <paramref name="id"/>. A built-in is not changed: a copy named
    /// after it (<c>Roleplay 2</c>) is saved instead, and the result says so, so the caller can re-point whoever
    /// was using the built-in at the copy.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No layout has <paramref name="id"/>.</exception>
    public LayoutSaveResult Update(
        string id,
        IEnumerable<PanelPlacement> placements,
        IReadOnlyDictionary<LayoutRegion, double>? regionShares = null)
    {
        ArgumentNullException.ThrowIfNull(placements);
        var existing = Find(id) ?? throw new KeyNotFoundException($"No layout has the id '{id}'.");
        if (existing.IsBuiltIn)
        {
            return new LayoutSaveResult(SaveAsNew(existing.Name, placements, regionShares ?? existing.RegionShares), true);
        }

        var updated = existing with
        {
            Placements = placements.ToArray(),
            RegionShares = regionShares is null ? existing.RegionShares : new Dictionary<LayoutRegion, double>(regionShares),
        };
        _user[IndexOf(id)] = updated;
        return new LayoutSaveResult(updated, false);
    }

    /// <summary>
    /// Renames a saved layout. Refused (false) for a built-in, an unknown id, a blank name or a name another
    /// layout already has; renaming to its own name in a different case is allowed.
    /// </summary>
    public bool Rename(string id, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || Find(id) is not { IsBuiltIn: false } existing
            || IsNameTaken(newName, exceptId: id))
        {
            return false;
        }

        _user[IndexOf(id)] = existing with { Name = newName.Trim() };
        return true;
    }

    /// <summary>
    /// Deletes a saved layout. Refused (false) for a built-in or an unknown id. Characters and worlds still naming
    /// it are left alone: <see cref="LayoutResolver"/> falls through past an id that names nothing.
    /// </summary>
    public bool Delete(string id)
    {
        if (Find(id) is not { IsBuiltIn: false })
        {
            return false;
        }

        _user.RemoveAt(IndexOf(id));
        return true;
    }

    private int IndexOf(string id) => _user.FindIndex(l => string.Equals(l.Id, id, StringComparison.Ordinal));
}
