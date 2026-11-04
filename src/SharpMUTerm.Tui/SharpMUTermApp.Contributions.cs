using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// The contribution registry this app reads its command surface from. The built-in commands are registered
/// at construction (<see cref="BuiltInCommands"/> says what each is called and when it is offered;
/// <see cref="RunBuiltInCommand"/> does the work), and ⌃P, the pointer menu and every
/// <see cref="DispatchCommand"/> read the registry rather than a list of their own — so a command a package or
/// a host adds is listed, searched, offered and run exactly as a built-in one is.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private readonly ContributionRegistry _contributions = new();

    /// <summary>
    /// What was last run from ⌃P, newest first, listed under RECENT when the surface opens with nothing typed.
    /// In memory for the run and never saved: see <see cref="RecentCommands"/> for why.
    /// </summary>
    private readonly RecentCommands _recentCommands = new();

    /// <summary>The ⌃P surface itself, for a test that reads what it is listing.</summary>
    internal CommandPalette Palette => _palette;

    /// <summary>The ids the RECENT group lists, newest first, for a test.</summary>
    internal IReadOnlyList<string> RecentCommandIds => _recentCommands.Ids;

    /// <summary>
    /// Runs an entry the way choosing it on ⌃P does: recorded among the recent ones, then dispatched. Internal
    /// so a test and the <c>menu-recent</c> snapshot run commands through the palette's own path.
    /// </summary>
    internal bool RunFromPalette(string id)
    {
        _recentCommands.Record(id);
        return DispatchCommand(id);
    }

    /// <summary>The registry the surfaces read. A host or a package adds to it; the views pick it up on their next read.</summary>
    internal ContributionRegistry Contributions => _contributions;

    private void RegisterBuiltInContributions() => BuiltInCommands.Register(_contributions, RunBuiltInCommand);

    /// <summary>What a command reads right now. Each value is worked out only if something asks for it.</summary>
    private ICommandContext CommandEnvironment() => new AppCommandContext(this);

    /// <summary>
    /// Runs a command-surface entry by its id, through the contribution that handles it. Internal so a
    /// headless test can dispatch an id the way the palette does, without opening the surface and typing at it.
    /// <para>
    /// Returns <see langword="false"/> for an id nothing implements, and says so on the status line — never
    /// through <c>_active</c>: an unwired command is exactly what you hit while nothing is connected, and a
    /// message that needs a session to be seen cannot report in the state it is being read in.
    /// <c>CommandDispatchTests</c> dispatches every id the catalog can emit and fails on a false.
    /// </para>
    /// </summary>
    internal bool DispatchCommand(string id)
    {
        if (_contributions.Commands.Resolve(id) is not { } contribution)
        {
            RefuseCommand($"{id} isn't wired in this build yet");
            RefreshTabTitles();
            return false;
        }

        if (contribution.Owner.IsBuiltIn)
        {
            return contribution.Execute(id, CommandEnvironment());
        }

        // A package's command is somebody else's code: a throw from it is reported, not allowed to take the
        // UI thread down with it.
        try
        {
            return contribution.Execute(id, CommandEnvironment());
        }
        catch (Exception ex)
        {
            RefuseCommand($"{id} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The key a pointer menu shows for a command: the one the contribution names on this host, else the key
    /// its catalog subtitle leads with (<see cref="ShortHint"/>), for an entry a family generated.
    /// </summary>
    private string? MenuKeyHint(CommandItem command) =>
        _contributions.Commands.Resolve(command.Id) is SingleCommand { Key: { } key }
            ? key.Resolve(HostKeyGlyphs())
            : ShortHint(command.Subtitle);

    private HostKeyGlyphs HostKeyGlyphs() => new(Host.Keys.Newline.Glyph);

    private sealed class AppCommandContext(SharpMUTermApp app) : ICommandContext
    {
        private CommandContext? _state;
        private IReadOnlyList<CharacterRef>? _characters;
        private IReadOnlyList<SettingsEntry>? _settings;
        private HostKeyGlyphs? _keys;

        public CommandContext State => _state ??= app.CommandState();

        public Workspace Workspace => app._workspace;

        public IReadOnlyList<CharacterRef> Characters => _characters ??= app.BuildCharacterRefs();

        public string? FocusedSessionKey => app._active?.SessionKey;

        public IReadOnlyList<SettingsEntry> Settings => _settings ??= app.SettingsCommands();

        public HostKeyGlyphs Keys => _keys ??= app.HostKeyGlyphs();
    }
}
