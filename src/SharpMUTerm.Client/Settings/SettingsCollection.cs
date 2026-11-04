using System.Globalization;
using System.Text.Json;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Settings;

/// <summary>
/// What a host knows that a list section's fields need and the configuration does not hold: the windows a trigger can
/// route to (the workspace's), the recent output a trigger's pattern is tested against (the panes'), and which chords
/// this host can deliver to a key binding. Captured on the client's thread when a surface opens.
/// </summary>
/// <param name="Windows">The windows a route can name beyond <c>(none)</c> and <c>main</c>.</param>
/// <param name="Lines">Recent output, oldest first, the newest <see cref="SharpMUTerm.Core.Automation.TriggerPreview.MaxLines"/> of which a pattern is tested against.</param>
/// <param name="KeyProblem">Why a chord cannot fire in this host, or null when it can.</param>
/// <param name="Bindable">The chords a new key binding may be born on, in the order a host offers them.</param>
public sealed record SettingsContext(
    IReadOnlyList<string> Windows,
    IReadOnlyList<string> Lines,
    Func<string, string?> KeyProblem,
    IReadOnlyList<string> Bindable)
{
    /// <summary>Nothing known: no windows, no output, every chord allowed, none offered to a new binding.</summary>
    public static SettingsContext Empty { get; } = new([], [], _ => null, []);
}

/// <summary>
/// One trigger set as a list section shows it: its items of the section's kind, and whether they may be edited. A
/// package's sets are listed read-only — a package update replaces them wholesale, so an edit there would be lost
/// — and offer a copy into the player's own sets instead.
/// </summary>
/// <param name="Set">The set.</param>
/// <param name="Label">What the list calls it: the set's name, and the package's for a package's set.</param>
/// <param name="ReadOnly">Whether it is a package's, and refuses edits.</param>
/// <param name="PackageId">The package it came from, or null for one of the player's own.</param>
/// <param name="Items">Its items of the section's kind, in order.</param>
public sealed record SettingsItemGroup(
    TriggerSet Set, string Label, bool ReadOnly, string? PackageId, IReadOnlyList<object> Items);

/// <summary>
/// A list section's declaration (F2's triggers, F3's aliases, F4's key bindings, F6's timers): the items, each held in
/// a <see cref="TriggerSet"/>; each item's fields as ordinary <see cref="SettingsField"/>s over
/// <see cref="SettingScope.Item"/> bindings; and add, duplicate, delete, reorder and enable. Both hosts draw a list
/// section from this — the cells as their list-and-editor screen, the window as a master-detail editor — so the two
/// list the same fields and edit the same objects.
/// <para>
/// <b>Lists are replaced, never edited in place</b>: an add, a delete or a move builds the set's new list and assigns
/// it, so a host reading the configuration on its own thread never meets a list half-way through a change. The
/// operations here are pure over the configuration; a surface runs them through its save funnel.
/// </para>
/// </summary>
public abstract class SettingsCollection
{
    /// <summary>What one item is called (<c>trigger</c>).</summary>
    public required string Noun { get; init; }

    /// <summary>What several are called (<c>triggers</c>).</summary>
    public required string Plural { get; init; }

    /// <summary>Every field of an item, in the order a form shows them.</summary>
    public required IReadOnlyList<SettingsField> Fields { get; init; }

    /// <summary>The id of the field that enables the item, which a list shows as each row's check box.</summary>
    public string EnabledField { get; init; } = "enabled";

    /// <summary>The id of the field naming the item.</summary>
    public string NameField { get; init; } = "name";

    /// <summary>Whether an item can be duplicated (a timer cannot: its copy would differ in everything).</summary>
    public bool CanDuplicate { get; init; } = true;

    /// <summary>A field by id.</summary>
    public SettingsField Field(string id) =>
        Fields.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"The {Plural} declare no field '{id}'.");

    /// <summary>The items of <paramref name="set"/>, in order.</summary>
    public abstract IReadOnlyList<object> ItemsIn(TriggerSet set);

    /// <summary>Whether <paramref name="item"/> is one of this collection's kind.</summary>
    public abstract bool IsItem(object item);

    /// <summary>What a list row says for <paramref name="item"/>: its name, or what it does when it has none.</summary>
    public abstract string TitleOf(object item);

    /// <summary>The second line a list row says (a pattern, an interval), or null.</summary>
    public abstract string? DetailOf(object item);

    /// <summary>Whether <paramref name="item"/> is enabled.</summary>
    public bool IsEnabled(AppConfiguration config, object item) =>
        Field(EnabledField).Binding is SettingBinding<bool> flag && flag.Get(new SettingTarget(config, Item: item));

    /// <summary>
    /// A new item for <paramref name="set"/>, named so it can be told from the rest, not yet added; null when none can
    /// be made (every chord a key binding could be born on is taken).
    /// </summary>
    public abstract object? Create(AppConfiguration config, SettingsContext context);

    /// <summary>A deep copy of <paramref name="item"/>, renamed, not yet added.</summary>
    public abstract object Duplicate(AppConfiguration config, object item);

    /// <summary>Replaces <paramref name="set"/>'s items with <paramref name="items"/>.</summary>
    protected abstract void Replace(TriggerSet set, IEnumerable<object> items);

    /// <summary>The set holding <paramref name="item"/> among <paramref name="sets"/>, or null.</summary>
    public TriggerSet? OwnerOf(IEnumerable<TriggerSet> sets, object item)
    {
        ArgumentNullException.ThrowIfNull(sets);
        return sets.FirstOrDefault(s => ItemsIn(s).Any(i => ReferenceEquals(i, item)));
    }

    /// <summary>Adds <paramref name="item"/> at the end of <paramref name="set"/>.</summary>
    public void Add(TriggerSet set, object item)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(item);
        Replace(set, [.. ItemsIn(set), item]);
    }

    /// <summary>Puts <paramref name="copy"/> straight after <paramref name="source"/> in the set that holds it.</summary>
    public void InsertAfter(AppConfiguration config, object source, object copy)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (OwnerOf(config.TriggerSets, source) is not { } set)
        {
            return;
        }

        var items = ItemsIn(set).ToList();
        items.Insert(items.FindIndex(i => ReferenceEquals(i, source)) + 1, copy);
        Replace(set, items);
    }

    /// <summary>Takes <paramref name="item"/> out of whichever of the configuration's sets holds it.</summary>
    public bool Delete(AppConfiguration config, object item)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (OwnerOf(config.TriggerSets, item) is not { } set)
        {
            return false;
        }

        Replace(set, ItemsIn(set).Where(i => !ReferenceEquals(i, item)));
        return true;
    }

    /// <summary>
    /// Moves <paramref name="item"/> <paramref name="delta"/> places within its set — order is meaning for a trigger
    /// (the first match that stops processing wins) — and false when it is already at that end.
    /// </summary>
    public bool Move(AppConfiguration config, object item, int delta)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (OwnerOf(config.TriggerSets, item) is not { } set)
        {
            return false;
        }

        var items = ItemsIn(set).ToList();
        var at = items.FindIndex(i => ReferenceEquals(i, item));
        var to = at + delta;
        if (to < 0 || to >= items.Count || to == at)
        {
            return false;
        }

        items.RemoveAt(at);
        items.Insert(to, item);
        Replace(set, items);
        return true;
    }

    /// <summary>Moves <paramref name="item"/> to the end of the set named <paramref name="name"/>.</summary>
    public void MoveTo(AppConfiguration config, object item, string name)
    {
        ArgumentNullException.ThrowIfNull(config);
        var target = config.TriggerSets.FirstOrDefault(s => string.Equals(s.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (target is null || OwnerOf(config.TriggerSets, item) is not { } owner || ReferenceEquals(owner, target))
        {
            return;
        }

        Replace(owner, ItemsIn(owner).Where(i => !ReferenceEquals(i, item)));
        Add(target, item);
    }

    /// <summary>
    /// <paramref name="name"/>, or — when another item already has it — the first of <c>name 2</c>, <c>name 3</c>… that
    /// none has. The cell screens' rule (<c>ScreenLists.Available</c>).
    /// </summary>
    public static string Available(IEnumerable<string> taken, string name)
    {
        ArgumentNullException.ThrowIfNull(taken);
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var candidate = name;
        for (var n = 2; used.Contains(candidate); n++)
        {
            candidate = $"{name} {n.ToString(CultureInfo.InvariantCulture)}";
        }

        return candidate;
    }

    /// <summary>
    /// A copy of a package's <paramref name="set"/> among the player's own, named <c>(copy)</c> when its name is taken
    /// — F10's <c>c</c>, one set at a time. The copy is a deep one, so editing it never touches the package's.
    /// </summary>
    public static TriggerSet CopyToEdit(AppConfiguration config, TriggerSet set)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(set);
        var copy = JsonSerializer.Deserialize<TriggerSet>(
            JsonSerializer.SerializeToUtf8Bytes(set, ConfigurationStore.SerializerOptions), ConfigurationStore.SerializerOptions)!;
        var name = set.Name;
        for (var n = 1; config.TriggerSets.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
        {
            name = n == 1 ? $"{set.Name} (copy)" : $"{set.Name} (copy {n.ToString(CultureInfo.InvariantCulture)})";
        }

        copy.Name = name;
        config.TriggerSets.Add(copy);
        return copy;
    }
}

/// <summary>A list section's declaration over items of type <typeparamref name="TItem"/>.</summary>
public sealed class SettingsCollection<TItem> : SettingsCollection
    where TItem : class
{
    /// <summary>The list of a set that holds the items.</summary>
    public required Func<TriggerSet, List<TItem>> Items { get; init; }

    /// <summary>Replaces that list.</summary>
    public required Action<TriggerSet, List<TItem>> SetItems { get; init; }

    /// <summary>A new item, before it is named; null when none can be made in this host.</summary>
    public required Func<SettingsContext, IEnumerable<TItem>, TItem?> New { get; init; }

    /// <summary>A deep copy.</summary>
    public required Func<TItem, TItem> Copy { get; init; }

    /// <summary>The item's name.</summary>
    public required Func<TItem, string> Name { get; init; }

    /// <summary>Renames the item.</summary>
    public required Action<TItem, string> Rename { get; init; }

    /// <summary>A list row's title when the name is blank, or null to show the name.</summary>
    public Func<TItem, string>? Title { get; init; }

    /// <summary>A list row's second line, or null.</summary>
    public Func<TItem, string?>? Detail { get; init; }

    /// <inheritdoc />
    public override IReadOnlyList<object> ItemsIn(TriggerSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return Items(set).ToArray();
    }

    /// <inheritdoc />
    public override bool IsItem(object item) => item is TItem;

    /// <inheritdoc />
    public override string TitleOf(object item)
    {
        var typed = (TItem)item;
        var name = Name(typed);
        return string.IsNullOrWhiteSpace(name) && Title is not null ? Title(typed) : name;
    }

    /// <inheritdoc />
    public override string? DetailOf(object item) => Detail?.Invoke((TItem)item);

    /// <inheritdoc />
    public override object? Create(AppConfiguration config, SettingsContext context)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(context);
        var all = config.TriggerSets.SelectMany(Items).ToList();
        if (New(context, all) is not { } item)
        {
            return null;
        }

        Rename(item, Available(all.Select(Name), Name(item)));
        return item;
    }

    /// <inheritdoc />
    public override object Duplicate(AppConfiguration config, object item)
    {
        ArgumentNullException.ThrowIfNull(config);
        var source = (TItem)item;
        var copy = Copy(source);
        Rename(copy, Available(config.TriggerSets.SelectMany(Items).Select(Name), Name(source) + " copy"));
        return copy;
    }

    /// <inheritdoc />
    protected override void Replace(TriggerSet set, IEnumerable<object> items) =>
        SetItems(set, items.Cast<TItem>().ToList());
}
