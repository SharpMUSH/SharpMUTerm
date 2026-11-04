using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Protocols;

namespace SharpMUTerm.Tui;

/// <summary>
/// The settings screens' declared half: the sections the client registers (<see cref="BuiltInSettings"/>), the
/// projections F7 and F8 are drawn from, and the settings search's way back into the screens. The screens'
/// keyboard and chrome are <see cref="SettingsOverlay"/>'s and <see cref="SettingsSession"/>'s as before.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private void RegisterBuiltInSettings() => BuiltInSettings.Register(_contributions.SettingsSections);

    /// <summary>A declared section's title — what its screen's header, its ⌃P entry and the search all call it.</summary>
    private string SectionTitle(string id) =>
        Section(id).Title;

    /// <summary>A declared section, from the registry; the built-in declaration if something removed it.</summary>
    private SettingsSectionContribution Section(string id) =>
        _contributions.SettingsSections.Find(id) ?? BuiltInSettings.All.First(s => s.Id == id);

    /// <summary>
    /// F7 as it stands: the declared section over the live configuration, the meter table reading the focused
    /// window's game data, and <paramref name="cursor"/> deciding whether the received-data box is shown.
    /// </summary>
    private OptionsScreenRenderer.OptionsScreen TextProjection(int cursor) =>
        OptionsScreenRenderer.TextAnsiScreen(_config, includeSound: true, SettingsGameData(), cursor, Section("text"));

    /// <summary>F8 as it stands.</summary>
    private OptionsScreenRenderer.OptionsScreen InputProjection() =>
        OptionsScreenRenderer.InputScreen(_config, Section("input"));

    /// <summary>
    /// The game data the meter editor reads: the focused window's session's — never <c>_active</c>'s, for the
    /// misdelivery reason everything else here resolves through the focused window — or the demo scene's.
    /// </summary>
    private GameDataStore SettingsGameData() => SendTarget()?.GameData ?? StatusData();

    /// <summary>What the settings search reads, and how its ⏎ lands.</summary>
    private SettingsSearchHost SettingsSearchHost() =>
        new(() => _contributions.SettingsSections.Ordered, () => _config, GoToSetting);

    /// <summary>
    /// The search's ⏎: opens the match's screen in the same window and, for a declared field, puts the cursor
    /// on the row drawing it. Packages (F10) is not a settings screen and is opened its own way.
    /// </summary>
    private bool GoToSetting(SettingsMatch match)
    {
        if (string.Equals(match.Section.Id, "packages", StringComparison.Ordinal))
        {
            _settings.CloseAll();
            ShowPackages();
            return true;
        }

        if (!Enum.TryParse<ConsoleKey>(match.Section.Key, out var key)
            || SettingsScreens().FirstOrDefault(s => s.Key == key) is not { Open: not null } screen)
        {
            return false;
        }

        var binding = screen.Open();
        if (match.Field is { } field && FieldRow(match.Section.Id, field.Id) is { } row and >= 0)
        {
            binding.Session.Selection.Seed(0, row);
        }

        _settings.Show(key, binding);
        return true;
    }

    /// <summary>Where a declared field's row is on its screen, or null for a section not drawn from declarations.</summary>
    private int? FieldRow(string section, string field) => section switch
    {
        "text" => OptionsScreenRenderer.IndexOf(TextProjection(-1), field),
        "input" => OptionsScreenRenderer.IndexOf(InputProjection(), field),
        _ => null,
    };

    /// <summary>Whether the settings search is up, for a test.</summary>
    internal bool SettingsSearchIsOpen => _settings.IsSearching;

    /// <summary>The F-key of the open settings screen, for a test.</summary>
    internal ConsoleKey? SettingsOpenKey => _settings.OpenKey;

    /// <summary>Where the open settings screen's cursor is, for a test.</summary>
    internal ScreenFocus? SettingsFocus => _settings.Focus;

    /// <summary>Where a declared field's row is on F7 or F8, for a test.</summary>
    internal int? SettingsFieldRow(string section, string field) => FieldRow(section, field);
}
