using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Text;

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

        // A declared section is the host's own settings window where the host has one.
        if (TryOpenNativeSettings(match.Section))
        {
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

    /// <summary>
    /// Opens the settings screen on <paramref name="key"/>: the host's own settings window when the host has one and
    /// the screen's section declares its fields, otherwise the cell screen, toggled as it always was. Every way in —
    /// the F-key, ⌃P's SETTINGS entries — comes through here, so a window host's F7 never shows the cell overlay.
    /// </summary>
    private void OpenSettingsScreen(ConsoleKey key, Func<ScreenBinding> open)
    {
        var name = key.ToString();
        var section = _contributions.SettingsSections.Ordered
            .FirstOrDefault(s => string.Equals(s.Key, name, StringComparison.Ordinal));
        if (!TryOpenNativeSettings(section))
        {
            _settings.Toggle(key, open);
        }
    }

    /// <summary>
    /// Shows <paramref name="section"/> in the host's settings window, closing any cell screen first; false when the
    /// host has none (the terminal, the snapshot, the tests) or the section declares no fields, which keep their
    /// cell screens.
    /// </summary>
    private bool TryOpenNativeSettings(SettingsSectionContribution? section)
    {
        if (Host.NativeSettings is not { } native || section is null || !SettingsForm.IsDeclared(section))
        {
            return false;
        }

        _settings.CloseAll();
        native.Show(NativeSettingsForm(section.Id));
        return true;
    }

    /// <summary>
    /// The declared settings as a form a host draws natively, opening on <paramref name="initial"/>. Every change it
    /// makes goes through a <see cref="ScreenEdits"/> over <see cref="SaveConfiguration"/> — the cell screens' own
    /// funnel — on this thread, so it is saved and reaches the bars and live sessions exactly as an F7 edit does. The
    /// meter table reads the game data of the window focused when the form opened (<see cref="SettingsGameData"/>),
    /// captured here because the store is thread-safe and the focus is not.
    /// </summary>
    internal SettingsForm NativeSettingsForm(string initial)
    {
        var edits = new ScreenEdits(SaveConfiguration);
        var data = SettingsGameData();
        SettingsForm? form = null;
        form = new SettingsForm(
            _contributions.SettingsSections.Ordered,
            _config,
            change => OnUiThread(() =>
            {
                edits.Apply(change);
                form?.NotifyChanged();
            }),
            id => OnUiThread(() => OpenSectionScreen(id)),
            initial,
            ThemeIsDark(),
            () => OptionsScreenRenderer.ReceivedPaths(data).Select(r => new ReceivedValue(r.Path, r.Value)).ToList(),
            meter => OptionsScreenRenderer.NowText(meter, data));
        return form;
    }

    /// <summary>
    /// The settings window's way to a section it cannot draw: close the window and open that section's own screen.
    /// </summary>
    private void OpenSectionScreen(string id)
    {
        Host.NativeSettings?.Close();
        if (_contributions.SettingsSections.Find(id) is { } section)
        {
            GoToSetting(new SettingsMatch(section, null, section.Title, -1, 0));
        }
    }

    /// <summary>Whether the client's theme is a dark one: its default background is darker than mid-grey.</summary>
    private bool ThemeIsDark()
    {
        var bg = _theme.Resolve(TerminalColor.Default, isBackground: true);
        return (0.2126 * bg.R) + (0.7152 * bg.G) + (0.0722 * bg.B) < 128;
    }

    /// <summary>Whether the settings search is up, for a test.</summary>
    internal bool SettingsSearchIsOpen => _settings.IsSearching;

    /// <summary>The F-key of the open settings screen, for a test.</summary>
    internal ConsoleKey? SettingsOpenKey => _settings.OpenKey;

    /// <summary>Where the open settings screen's cursor is, for a test.</summary>
    internal ScreenFocus? SettingsFocus => _settings.Focus;

    /// <summary>Where a declared field's row is on F7 or F8, for a test.</summary>
    internal int? SettingsFieldRow(string section, string field) => FieldRow(section, field);
}
