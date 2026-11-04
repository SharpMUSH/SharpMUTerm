using SharpMUTerm.Client.Contributions;
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
    private WorldDefinition? _world;
    private CharacterDefinition? _character;

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
        CharacterDefinition? character = null)
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

    /// <summary>The id of the section shown first.</summary>
    public string Initial { get; }

    /// <summary>Whether the client's theme is dark — now, so a theme picked here is followed.</summary>
    public bool IsDark => _isDark();

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
    private SettingTarget Target => new(_config, _world, _character);

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
        return section.Fields.Count > 0;
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
    public void Set<T>(SettingBinding<T> binding, T value)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var target = Target;
        _commit(() => binding.Set(target, value));
    }

    /// <summary>Writes a number, held to <paramref name="field"/>'s bounds.</summary>
    public void SetNumber(NumberField field, int value)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Binding is SettingBinding<int> binding)
        {
            Set(binding, Math.Clamp(value, field.Minimum, field.Maximum));
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

        if (TextRules.Check(field, value) is { } refused)
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
