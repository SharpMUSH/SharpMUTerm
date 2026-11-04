using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Settings;

/// <summary>A value the focused connection's game data holds, as a path a meter can name and its text now.</summary>
/// <param name="Path">The path, as a meter names it (<c>Char.Vitals.hp</c>).</param>
/// <param name="Value">What it holds now.</param>
public sealed record ReceivedValue(string Path, string Value);

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

    /// <param name="sections">Every settings section, in order — declared ones and the ones only listed by title.</param>
    /// <param name="config">The live configuration the values are read from and written to.</param>
    /// <param name="commit">Runs a change to the configuration through the client's save funnel.</param>
    /// <param name="openScreen">Opens a section's own screen, by id — the way to a section this form cannot draw.</param>
    /// <param name="initial">The section to show first.</param>
    /// <param name="isDark">Whether the client's theme is a dark one, so the host's controls can follow it.</param>
    /// <param name="received">The focused connection's game data as meter paths, or null for none.</param>
    /// <param name="now">What a meter reads now (<c>412/500</c>), or null while its value has not arrived.</param>
    public SettingsForm(
        IReadOnlyList<SettingsSectionContribution> sections,
        AppConfiguration config,
        Action<Action> commit,
        Action<string> openScreen,
        string initial,
        bool isDark,
        Func<IReadOnlyList<ReceivedValue>>? received = null,
        Func<StatusMeter, string?>? now = null)
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
        IsDark = isDark;
        _received = received ?? (() => Array.Empty<ReceivedValue>());
        _now = now ?? (_ => null);
    }

    /// <summary>Every section, in order.</summary>
    public IReadOnlyList<SettingsSectionContribution> Sections { get; }

    /// <summary>The id of the section shown first.</summary>
    public string Initial { get; }

    /// <summary>Whether the client's theme is dark.</summary>
    public bool IsDark { get; }

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
        return binding.Get(_config);
    }

    /// <summary>Whether <paramref name="field"/> holds its default — what decides its changed dot and its reset.</summary>
    public bool IsDefault(SettingsField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Binding?.IsDefault(_config) ?? true;
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
    /// surface can store what the other would refuse.
    /// </summary>
    public void Set<T>(SettingBinding<T> binding, T value)
    {
        ArgumentNullException.ThrowIfNull(binding);
        _commit(() => binding.Set(_config, value));
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

    /// <summary>Writes a text; empty is stored as no value, the cell screen's optional-field rule.</summary>
    public void SetText(TextField field, string? value)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Binding is not SettingBinding<string?> binding)
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (text is not null && field.MaxLength is { } max && text.Length > max)
        {
            text = text[..max];
        }

        Set(binding, text);
    }

    /// <summary>Puts <paramref name="field"/> back to its default, through the funnel, as the cell screen's <c>r</c> does.</summary>
    public void Reset(SettingsField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Binding is { } binding)
        {
            _commit(() => binding.Reset(_config));
        }
    }

    /// <summary>The meters now — a copy, so a host can edit it and hand it back to <see cref="SetMeters"/>.</summary>
    public List<StatusMeter> Meters(ListField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Binding is SettingBinding<List<StatusMeter>> binding
            ? binding.Get(_config).Select(Copy).ToList()
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
