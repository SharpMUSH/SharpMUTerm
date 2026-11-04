using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Settings;

/// <summary>A value the focused connection's game data holds, as a path a meter can name and its text now.</summary>
/// <param name="Path">The path, as a meter names it (<c>Char.Vitals.hp</c>).</param>
/// <param name="Value">What it holds now.</param>
public sealed record ReceivedValue(string Path, string Value);

/// <summary>A world or character a scoped section can be edited for, as a host lists it.</summary>
/// <param name="Label">What the list says: the world's name, or <c>Corvid · Aetherfall</c>.</param>
/// <param name="World">The world.</param>
/// <param name="Character">The character, for a character-scoped section; null for a world.</param>
public sealed record SettingsSubject(string Label, WorldDefinition World, CharacterDefinition? Character);

/// <summary>
/// The declared settings as a form a host draws with its own controls: the sections in their order, every value
/// read through its field's <see cref="SettingBinding{T}"/>, and every change committed through one delegate the
/// client supplies — the same funnel its cell screens commit through, so a change made here is saved, re-syncs the
/// bars and reloads automation exactly as one made on F7 does. Nothing here knows what a control is.
/// <para>
/// <b>Threads.</b> A host may read values on its own thread: a declared value is a scalar, or a list that is only
/// ever replaced wholesale (this form never edits one in place), so a read never meets a half-made change. Every
/// write goes through <c>commit</c>, which the client runs on its own thread, and <see cref="Changed"/> is raised
/// there once a change has landed.
/// </para>
/// </summary>
public sealed class SettingsForm
{
    private readonly AppConfiguration _config;
    private readonly Action<Action> _commit;
    private readonly Action<string> _openScreen;
    private readonly Func<IReadOnlyList<ReceivedValue>> _received;
    private readonly Func<StatusMeter, string?> _now;
    private readonly Func<IReadOnlyList<ThemeCard>> _themes;
    private readonly Func<bool> _isDark;
    private readonly SettingsContext _context;
    private WorldDefinition? _world;
    private CharacterDefinition? _character;
    private object? _item;

    /// <param name="sections">Every settings section, in order — declared ones and the ones only listed by title.</param>
    /// <param name="config">The live configuration the values are read from and written to.</param>
    /// <param name="commit">Runs a change to the configuration through the client's save funnel.</param>
    /// <param name="openScreen">Opens a section's own screen, by id — the way to a section this form cannot draw.</param>
    /// <param name="initial">The section to show first.</param>
    /// <param name="isDark">Whether the client's theme is a dark one, so the host's controls can follow it.</param>
    /// <param name="received">The focused connection's game data as meter paths, or null for none.</param>
    /// <param name="now">What a meter reads now (<c>412/500</c>), or null while its value has not arrived.</param>
    /// <param name="themes">The themes the theme field offers, each with its preview, or null for none.</param>
    /// <param name="isDarkNow">Whether the client's theme is dark now — it changes when a theme is picked here.</param>
    /// <param name="world">The world a world-scoped section is edited for first, or null for the first world.</param>
    /// <param name="character">The character a character-scoped section is edited for first, or null for the first.</param>
    /// <param name="context">What the host knows that a list section's fields need (windows, recent output, chords).</param>
    /// <param name="item">The item a list section opens on (the trigger builder's new rule), or null for the first.</param>
    public SettingsForm(
        IReadOnlyList<SettingsSectionContribution> sections,
        AppConfiguration config,
        Action<Action> commit,
        Action<string> openScreen,
        string initial,
        bool isDark,
        Func<IReadOnlyList<ReceivedValue>>? received = null,
        Func<StatusMeter, string?>? now = null,
        Func<IReadOnlyList<ThemeCard>>? themes = null,
        Func<bool>? isDarkNow = null,
        WorldDefinition? world = null,
        CharacterDefinition? character = null,
        SettingsContext? context = null,
        object? item = null)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(openScreen);
        Sections = sections;
        _config = config;
        _commit = commit;
        _openScreen = openScreen;
        Initial = initial;
        _isDark = isDarkNow ?? (() => isDark);
        _received = received ?? (() => Array.Empty<ReceivedValue>());
        _now = now ?? (_ => null);
        _themes = themes ?? (() => Array.Empty<ThemeCard>());
        _context = context ?? SettingsContext.Empty;
        _item = item;
        var worlds = config.Worlds.ToArray();
        _character = character;
        _world = world ?? (character is null ? null : worlds.FirstOrDefault(w => w.Characters.Contains(character)))
            ?? worlds.FirstOrDefault();
        _character ??= _world?.Characters.FirstOrDefault() ?? worlds.SelectMany(w => w.Characters).FirstOrDefault();
        if (_character is not null && _world?.Characters.Contains(_character) != true)
        {
            _world = worlds.FirstOrDefault(w => w.Characters.Contains(_character)) ?? _world;
        }
    }

    /// <summary>Every section, in order.</summary>
    public IReadOnlyList<SettingsSectionContribution> Sections { get; }

    /// <summary>
    /// F10's page: the installed packages, the index once asked for, and the review waiting for consent — or null when
    /// the client offers none, and the page says so.
    /// </summary>
    public Packages.PackageManager? Packages { get; init; }

    /// <summary>The id of the section shown first.</summary>
    public string Initial { get; }

    /// <summary>Whether the client's theme is dark — now, so a theme picked here is followed.</summary>
    public bool IsDark => _isDark();

    /// <summary>
    /// The client's theme now — a copy, read from the host's thread — so the host derives its colours from it as the
    /// cells do and follows a theme picked here; null leaves the host the built-in theme of <see cref="IsDark"/>'s variant.
    /// </summary>
    public Func<Core.Theming.Theme>? Theme { get; init; }

    /// <summary>
    /// Whether the form was opened on an item made from what is on screen (the trigger builder's new rule), so the host
    /// should leave the output it was made from in view rather than cover it.
    /// </summary>
    public bool BesideOutput { get; init; }

    /// <summary>
    /// Every world (for <see cref="SettingScope.World"/>) or every character (for <see cref="SettingScope.Character"/>)
    /// a scoped section can be edited for, in configuration order; empty for an app-wide section.
    /// </summary>
    public IReadOnlyList<SettingsSubject> Subjects(SettingScope scope)
    {
        var worlds = _config.Worlds.ToArray();
        return scope switch
        {
            SettingScope.World => [.. worlds.Select(w => new SettingsSubject(w.Name, w, null))],
            SettingScope.Character =>
                [.. worlds.SelectMany(w => w.Characters.ToArray().Select(c => new SettingsSubject($"{c.Name} · {w.Name}", w, c)))],
            _ => [],
        };
    }

    /// <summary>The world or character a scoped section is being edited for, or null when there is none to edit.</summary>
    public SettingsSubject? Subject(SettingScope scope) => scope switch
    {
        SettingScope.World when _world is { } w => new SettingsSubject(w.Name, w, null),
        SettingScope.Character when _character is { } c && _world is { } w => new SettingsSubject($"{c.Name} · {w.Name}", w, c),
        _ => null,
    };

    /// <summary>
    /// Edits the scoped sections for <paramref name="subject"/> from now on: a world's fields for its world, a
    /// character's for its character (and its world).
    /// </summary>
    public void Choose(SettingsSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        _world = subject.World;
        if (subject.Character is { } character)
        {
            _character = character;
        }
        else if (_character is null || !subject.World.Characters.Contains(_character))
        {
            _character = subject.World.Characters.FirstOrDefault() ?? _character;
        }
    }

    /// <summary>Whether a section can be edited now: an app-wide one always, a scoped one once it has a world or character.</summary>
    public bool HasSubject(SettingsSectionContribution section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return Target.Reaches(section.Scope);
    }

    /// <summary>Where every binding reads and writes now: the configuration and the chosen world and character.</summary>
    private SettingTarget Target => new(_config, _world, _character, _item);

    /// <summary>The choices <paramref name="field"/> offers now (a layout row lists the player's own layouts).</summary>
    public IReadOnlyList<string> Choices(ChoiceField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.ChoicesIn(_config);
    }

    /// <summary>The themes the theme field offers, each with its preview — the model the cell picker draws too.</summary>
    public IReadOnlyList<ThemeCard> Themes() => _themes();

    /// <summary>Makes <paramref name="name"/> the client's theme, through the save funnel.</summary>
    public void SetTheme(ThemeField field, string name)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(name);
        if (field.Binding is SettingBinding<string> binding)
        {
            Set(binding, name);
        }
    }

    /// <summary>Raised on the client's thread after a change made through this form has landed.</summary>
    public event Action? Changed;

    /// <summary>Whether a section declares its fields, and so can be drawn here; the rest open their own screen.</summary>
    public static bool IsDeclared(SettingsSectionContribution section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return section.Fields.Count > 0 || section.Collection is not null || section.Packages is not null;
    }

    /// <summary>A section by id, or null.</summary>
    public SettingsSectionContribution? Section(string id) =>
        Sections.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    /// <summary>The value of <paramref name="binding"/> now.</summary>
    public T Get<T>(SettingBinding<T> binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding.Get(Target);
    }

    /// <summary>Whether <paramref name="field"/> holds its default — what decides its changed dot and its reset.</summary>
    public bool IsDefault(SettingsField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Binding?.IsDefault(Target) ?? true;
    }

    /// <summary>
    /// Notes the client attaches to a world field for one world — F5's <c>join wraps</c> recommendation while the pane is
    /// narrower than the server wraps — keyed by field id, then world name. Captured on the client's thread when the form
    /// opens, from the data its cell screen draws the same sentence from, so the window reads nothing live.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> WorldNotes { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    /// <summary>
    /// The note beside <paramref name="field"/> for the world being edited, or null. A note recommends changing the
    /// setting, so it holds only while the field is still at its default — once taken, it has nothing left to say.
    /// </summary>
    public string? Note(SettingsField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return _world is { } world
            && WorldNotes.TryGetValue(field.Id, out var byWorld)
            && byWorld.TryGetValue(world.Name, out var note)
            && IsDefault(field)
                ? note
                : null;
    }

    /// <summary>The default of <paramref name="field"/> as text, for a reset button's tooltip.</summary>
    public static string DefaultText(SettingsField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Binding?.DefaultText ?? string.Empty;
    }

    /// <summary>
    /// Writes <paramref name="value"/> through the save funnel. Numbers and texts go through <see cref="SetNumber"/>
    /// and <see cref="SetText"/>, which hold them to the rules the cell screen's field applies first, so neither
    /// surface can store what the other would refuse. A scoped field is written to the world or character chosen
    /// when the change was made, even if the choice moves before the client runs it.
    /// </summary>
    /// <returns>False when the value is not written: an item of a package's set (<see cref="IsReadOnly"/>).</returns>
    public bool Set<T>(SettingBinding<T> binding, T value)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var target = Target;
        if (binding.Scope == SettingScope.Item && (target.Item is null || IsReadOnly(target.Item)))
        {
            return false;
        }

        _commit(() => binding.Set(target, value));
        return true;
    }

    /// <summary>Writes a number, held to <paramref name="field"/>'s bounds.</summary>
    public void SetNumber(NumberField field, int value)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Binding is SettingBinding<int> binding)
        {
            _ = Set(binding, Math.Clamp(value, field.Minimum, field.Maximum));
        }
    }

    /// <summary>
    /// Writes a text by its field's <see cref="TextRule"/> — refused, trimmed, blank as no value — the rule the cell
    /// screen's field of the same kind applies. Returns why it was refused, or null once it is on its way.
    /// </summary>
    public string? SetText(TextField field, string? value)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Binding is not SettingBinding<string?> binding)
        {
            return null;
        }

        if (field.Binding.Scope == SettingScope.Item && _item is { } item && IsReadOnly(item))
        {
            return ReadOnlyRefusal;
        }

        if (TextRules.Check(field, value, Target) is { } refused)
        {
            return refused;
        }

        Set(binding, TextRules.Store(field, value));
        return null;
    }

    /// <summary>What a text field shows now: its value, or the fallback a defaulted field stands for.</summary>
    public string Text(TextField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Binding is SettingBinding<string?> binding ? TextRules.Shown(field, Get(binding)) : string.Empty;
    }

    /// <summary>Puts <paramref name="field"/> back to its default, through the funnel, as the cell screen's <c>r</c> does.</summary>
    public void Reset(SettingsField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Binding is { } binding)
        {
            var target = Target;
            _commit(() => binding.Reset(target));
        }
    }

    // ── list sections ────────────────────────────────────────────────────────────────────────────

    /// <summary>What an edit to a package's set is refused with.</summary>
    public const string ReadOnlyRefusal = "this set is a package's and is replaced when it updates — copy it to edit";

    /// <summary>What the host knows that a list section's fields need.</summary>
    public SettingsContext Context => _context;

    /// <summary>The item a list section's fields edit now, or null.</summary>
    public object? Item => _item;

    /// <summary>Edits <paramref name="item"/>'s fields from now on (null for none).</summary>
    public void Select(object? item) => _item = item;

    /// <summary>
    /// The item each list section last had selected, by section id, for the run: the host's, so it outlives this form
    /// and a list opened again (or a section switched back to) lands where it was left. Null keeps nothing. Read and
    /// written on the window's thread only.
    /// </summary>
    public IDictionary<string, object>? Selections { get; init; }

    /// <summary>The item <paramref name="sectionId"/> last had selected, or null.</summary>
    public object? Recall(string sectionId) =>
        Selections is { } kept && kept.TryGetValue(sectionId, out var item) ? item : null;

    /// <summary>Keeps <paramref name="item"/> as <paramref name="sectionId"/>'s selection for the run.</summary>
    public void Remember(string sectionId, object? item)
    {
        if (Selections is { } kept && item is not null)
        {
            kept[sectionId] = item;
        }
    }

    /// <summary>
    /// <paramref name="section"/>'s items, by set: the player's own sets in order, then every installed package's sets,
    /// read-only. Each set's items are a snapshot of its list now.
    /// </summary>
    public IReadOnlyList<SettingsItemGroup> Groups(SettingsSectionContribution section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.Collection is not { } collection)
        {
            return [];
        }

        var groups = _config.TriggerSets.ToArray()
            .Select(s => new SettingsItemGroup(s, s.Name, false, null, collection.ItemsIn(s)))
            .ToList();
        foreach (var package in _config.Packages)
        {
            groups.AddRange(package.Sets.Select(s =>
                new SettingsItemGroup(s, $"{s.Name} · {package.Manifest.Name}", true, package.Manifest.Id, collection.ItemsIn(s))));
        }

        return groups;
    }

    /// <summary>Whether <paramref name="item"/> lives in a package's set, and so refuses edits.</summary>
    public bool IsReadOnly(object item) =>
        _config.Packages.Any(p => p.Sets.Any(s => ItemsOf(s).Any(i => ReferenceEquals(i, item))));

    /// <summary>
    /// Adds a new item to <paramref name="set"/> through the funnel and selects it; null when the set is a package's or
    /// no item can be made (every chord a binding could be born on is taken).
    /// </summary>
    public object? Add(SettingsSectionContribution section, TriggerSet set)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(set);
        if (section.Collection is not { } collection || IsPackageSet(set)
            || collection.Create(_config, _context) is not { } item)
        {
            return null;
        }

        _item = item;
        _commit(() => collection.Add(set, item));
        return item;
    }

    /// <summary>Adds a copy of <paramref name="item"/> after it through the funnel and selects it; null when refused.</summary>
    public object? Duplicate(SettingsSectionContribution section, object item)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(item);
        if (section.Collection is not { CanDuplicate: true } collection || IsReadOnly(item))
        {
            return null;
        }

        var copy = collection.Duplicate(_config, item);
        _item = copy;
        _commit(() => collection.InsertAfter(_config, item, copy));
        return copy;
    }

    /// <summary>Deletes <paramref name="item"/> through the funnel; false when it is a package's.</summary>
    public bool Delete(SettingsSectionContribution section, object item)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(item);
        if (section.Collection is not { } collection || IsReadOnly(item))
        {
            return false;
        }

        if (ReferenceEquals(_item, item))
        {
            _item = null;
        }

        _commit(() => collection.Delete(_config, item));
        return true;
    }

    /// <summary>Moves <paramref name="item"/> up (−1) or down (+1) within its set, through the funnel.</summary>
    public bool Move(SettingsSectionContribution section, object item, int delta)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(item);
        if (section.Collection is not { } collection || IsReadOnly(item)
            || collection.OwnerOf(_config.TriggerSets, item) is not { } set)
        {
            return false;
        }

        var items = collection.ItemsIn(set).ToList();
        var at = items.FindIndex(i => ReferenceEquals(i, item));
        if (at + delta < 0 || at + delta >= items.Count)
        {
            return false;
        }

        _commit(() => collection.Move(_config, item, delta));
        return true;
    }

    /// <summary>Switches <paramref name="item"/> on or off through the funnel; false when it is a package's.</summary>
    public bool SetEnabled(SettingsSectionContribution section, object item, bool on)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(item);
        if (section.Collection?.Field(section.Collection.EnabledField).Binding is not SettingBinding<bool> flag
            || IsReadOnly(item))
        {
            return false;
        }

        var target = new SettingTarget(_config, _world, _character, item);
        _commit(() => flag.Set(target, on));
        return true;
    }

    /// <summary>Whether <paramref name="item"/> is enabled now.</summary>
    public bool IsEnabled(SettingsSectionContribution section, object item)
    {
        ArgumentNullException.ThrowIfNull(section);
        return section.Collection?.IsEnabled(_config, item) ?? false;
    }

    /// <summary>
    /// Copies a package's <paramref name="set"/> into the player's own sets through the funnel, where it can be edited.
    /// The copy is made on the client's thread with the commit; false when <paramref name="set"/> is not a package's.
    /// </summary>
    public bool CopyToEdit(TriggerSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (!IsPackageSet(set))
        {
            return false;
        }

        _commit(() => SettingsCollection.CopyToEdit(_config, set));
        return true;
    }

    /// <summary>The suggestions <paramref name="field"/> offers for the item selected now.</summary>
    public IReadOnlyList<string> Suggestions(TextField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Suggest is { } suggest && Target.Reaches(field.Binding?.Scope ?? SettingScope.App)
            ? suggest(Target, _context)
            : [];
    }

    /// <summary>Writes a fractional number, held to <paramref name="field"/>'s bounds.</summary>
    public bool SetDecimal(DecimalField field, double value)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Binding is SettingBinding<double> binding
            && Set(binding, Math.Clamp(value, field.Minimum, field.Maximum));
    }

    /// <summary>
    /// Sets a key binding's chord, refused (with the reason) when this host cannot deliver it or another binding holds
    /// it — the two refusals the cell screen's key capture makes.
    /// </summary>
    public string? SetKey(KeyField field, string chord)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(chord);
        if (field.Binding is not SettingBinding<string> binding || _item is not { } item)
        {
            return null;
        }

        if (IsReadOnly(item))
        {
            return ReadOnlyRefusal;
        }

        var canonical = MacroKey.Canonicalise(chord) ?? chord.Trim();
        if (_context.KeyProblem(canonical) is { } problem)
        {
            return problem;
        }

        if (_config.TriggerSets.SelectMany(s => s.Macros)
            .FirstOrDefault(m => !ReferenceEquals(m, item) && string.Equals(m.Key, canonical, StringComparison.OrdinalIgnoreCase)) is { } holder)
        {
            return $"already bound to {(string.IsNullOrWhiteSpace(holder.Name) ? holder.Key : holder.Name)}";
        }

        _ = Set(binding, canonical);
        return null;
    }

    /// <summary>
    /// The live test for the trigger selected now, with <paramref name="pattern"/> in place of its own while one is being
    /// typed: how many of the recent lines it matches, and the newest few with where.
    /// </summary>
    public TriggerPreviewResult? Preview(string? pattern = null, int samples = 6) =>
        _item is Trigger trigger
            ? TriggerPreview.Run(_context.Lines, trigger.MatchType, pattern ?? trigger.Pattern, trigger.CaseSensitive, samples)
            : null;

    private bool IsPackageSet(TriggerSet set) => _config.Packages.Any(p => p.Sets.Contains(set));

    private static IEnumerable<object> ItemsOf(TriggerSet set) =>
        set.Triggers.Cast<object>().Concat(set.Aliases).Concat(set.Timers).Concat(set.Macros);


    /// <summary>The meters now — a copy, so a host can edit it and hand it back to <see cref="SetMeters"/>.</summary>
    public List<StatusMeter> Meters(ListField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Binding is SettingBinding<List<StatusMeter>> binding
            ? binding.Get(Target).Select(Copy).ToList()
            : new List<StatusMeter>();
    }

    /// <summary>
    /// Replaces the meters with <paramref name="meters"/> (copied, labels clipped, empty maximum paths dropped), as
    /// one change through the funnel. The list in the configuration is replaced, never edited in place.
    /// </summary>
    public void SetMeters(ListField field, IEnumerable<StatusMeter> meters)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(meters);
        if (field.Binding is not SettingBinding<List<StatusMeter>> binding)
        {
            return;
        }

        var list = meters.Select(m => new StatusMeter
        {
            Label = StatusMeter.Clip(m.Label.Trim()),
            Value = m.Value.Trim(),
            Max = string.IsNullOrWhiteSpace(m.Max) ? null : m.Max.Trim(),
        }).ToList();
        Set(binding, list);
    }

    /// <summary>The focused connection's game data as paths a meter can name, sorted by path.</summary>
    public IReadOnlyList<ReceivedValue> Received() => _received();

    /// <summary>What <paramref name="meter"/> reads now, or null while its value has not arrived.</summary>
    public string? Now(StatusMeter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        return _now(meter);
    }

    /// <summary>The settings search, over these sections: the cell surface's own matching.</summary>
    public IReadOnlyList<SettingsMatch> Search(string? query) => SettingsSearch.Find(Sections, query);

    /// <summary>Opens a section's own screen, by id.</summary>
    public void OpenScreen(string sectionId)
    {
        ArgumentNullException.ThrowIfNull(sectionId);
        _openScreen(sectionId);
    }

    /// <summary>Raises <see cref="Changed"/>; the client calls it once a committed change has landed.</summary>
    public void NotifyChanged() => Changed?.Invoke();

    private static StatusMeter Copy(StatusMeter m) => new() { Label = m.Label, Value = m.Value, Max = m.Max };
}
