using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Client.Contributions;

/// <summary>A key whose chord differs between hosts. Each host says how it spells each one (<see cref="HostKeyGlyphs"/>).</summary>
public enum HostKey
{
    /// <summary>The chord that inserts a newline in the command line: ⌥⏎ in a terminal, ⌃⏎ in the desktop window.</summary>
    Newline,
}

/// <summary>
/// How the running host names its <see cref="HostKey"/>s on an in-app surface. Supplied by the host (the Tui
/// reads it off <c>ClientHost.Keys</c>), never guessed here, so a surface cannot name another host's chord.
/// </summary>
/// <param name="Newline">The newline chord as a surface writes it (<c>Alt+⏎</c>).</param>
public sealed record HostKeyGlyphs(string Newline)
{
    /// <summary>A terminal's spelling, for a caller that has no host to ask (a test of the catalog alone).</summary>
    public static HostKeyGlyphs Terminal { get; } = new("Alt+⏎");

    /// <summary>The glyph for <paramref name="key"/>.</summary>
    public string Glyph(HostKey key) => key switch
    {
        HostKey.Newline => Newline,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}

/// <summary>
/// The key that runs a command, as a surface names it: either the same on every host (<c>⌥R</c>, <c>⌃B |</c>,
/// <c>F1</c>) or a <see cref="HostKey"/> each host spells its own way.
/// </summary>
public sealed record KeyHint
{
    private KeyHint(string? glyph, HostKey? hostKey)
    {
        Fixed = glyph;
        Host = hostKey;
    }

    /// <summary>The glyph, when it is the same on every host.</summary>
    public string? Fixed { get; }

    /// <summary>The host key, when each host spells it differently.</summary>
    public HostKey? Host { get; }

    /// <summary>A chord spelt the same on every host.</summary>
    public static KeyHint Of(string glyph)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glyph);
        return new KeyHint(glyph, null);
    }

    /// <summary>A chord each host spells its own way.</summary>
    public static KeyHint ForHost(HostKey key) => new(null, key);

    /// <summary>The glyph on the host whose spellings are <paramref name="keys"/>.</summary>
    public string Resolve(HostKeyGlyphs keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return Fixed ?? keys.Glyph(Host!.Value);
    }

    /// <summary>Lets a literal stand for a fixed hint: <c>Key = "⌥R"</c>.</summary>
    public static implicit operator KeyHint(string glyph) => Of(glyph);
}

/// <summary>
/// What a command reads to decide whether it is offered and what it is called right now. A host supplies one
/// each time the catalog is built or a command is run. Kept small on purpose: a command's <em>work</em> is
/// done by its own delegate, which closes over whatever it needs; this is only the state every surface shares.
/// </summary>
public interface ICommandContext
{
    /// <summary>The stateful flags (logging, zoom, freeze, …) the catalog's labels read.</summary>
    CommandContext State { get; }

    /// <summary>The workspace: windows, panes, focus.</summary>
    Workspace Workspace { get; }

    /// <summary>Every configured character, in the order the connection rail draws them.</summary>
    IReadOnlyList<CharacterRef> Characters { get; }

    /// <summary>The session key of the character the surface is acting on, or null.</summary>
    string? FocusedSessionKey { get; }

    /// <summary>The settings screens the host offers, in its F-key order.</summary>
    IReadOnlyList<SettingsEntry> Settings { get; }

    /// <summary>How this host spells the keys that differ between hosts.</summary>
    HostKeyGlyphs Keys { get; }
}

/// <summary>A plain <see cref="ICommandContext"/>: every value given up front.</summary>
public sealed record CommandEnvironment(
    CommandContext State,
    Workspace Workspace,
    IReadOnlyList<CharacterRef> Characters,
    string? FocusedSessionKey,
    IReadOnlyList<SettingsEntry> Settings,
    HostKeyGlyphs Keys) : ICommandContext;

/// <summary>
/// <b>Commands</b> are the actions the command surface (⌃P) lists, searches and runs, and that a pointer menu
/// offers by id. A contribution is one of two shapes: a <see cref="SingleCommand"/>, one entry with a fixed id,
/// or a <see cref="CommandFamily"/>, a set of entries generated from live state (one per character, window or
/// pane) whose ids share a prefix. Either way the surface sees <see cref="CommandItem"/>s, in registration order.
/// </summary>
public abstract class CommandContribution : IContribution
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <inheritdoc />
    public ContributionOwner Owner { get; init; } = ContributionOwner.BuiltIn;

    /// <summary>The entries this contribution offers right now.</summary>
    public abstract IEnumerable<CommandItem> Entries(ICommandContext context);

    /// <summary>Whether a command id is one this contribution runs. Asked without a context: an id is the whole address.</summary>
    public abstract bool Handles(string commandId);

    /// <summary>
    /// Runs the command with this id. Availability is <em>not</em> checked: a surface offers what is available,
    /// but an id that arrives another way (a rail row, a key, the opposite half of a stateful pair) still runs.
    /// Returns false when it did not do what its label promised.
    /// </summary>
    public abstract bool Execute(string commandId, ICommandContext context);
}

/// <summary>One command with a fixed id: a title, the group it is listed under, the key that runs it and when it is offered.</summary>
public sealed class SingleCommand : CommandContribution
{
    /// <summary>What the surface calls it.</summary>
    public required string Title { get; init; }

    /// <summary>A title that depends on state, used instead of <see cref="Title"/> when set (a toggle that names its next state).</summary>
    public Func<ICommandContext, string>? TitleFor { get; init; }

    /// <summary>The group it is listed under.</summary>
    public required CommandGroup Category { get; init; }

    /// <summary>The key that runs it, on each host; null when nothing but the surface does.</summary>
    public KeyHint? Key { get; init; }

    /// <summary>A few words on what it does, shown beside the key.</summary>
    public string? Detail { get; init; }

    /// <summary>Whether the subtitle reads <c>detail · key</c> rather than the usual <c>key · detail</c>.</summary>
    public bool DetailFirst { get; init; }

    /// <summary>Whether it is offered right now; null means always.</summary>
    public Func<ICommandContext, bool>? IsAvailable { get; init; }

    /// <summary>Does the work. Returns false when it did not do what its label promised.</summary>
    public required Func<ICommandContext, bool> Run { get; init; }

    /// <summary>The subtitle the surface shows: the key and the detail, joined.</summary>
    public string? SubtitleFor(ICommandContext context)
    {
        var key = Key?.Resolve(context.Keys);
        return (key, Detail) switch
        {
            (null, null) => null,
            (null, { } detail) => detail,
            ({ } k, null) => k,
            ({ } k, { } detail) => DetailFirst ? $"{detail} · {k}" : $"{k} · {detail}",
        };
    }

    /// <inheritdoc />
    public override IEnumerable<CommandItem> Entries(ICommandContext context)
    {
        if (IsAvailable is null || IsAvailable(context))
        {
            yield return new CommandItem(Category, TitleFor?.Invoke(context) ?? Title, Id, SubtitleFor(context));
        }
    }

    /// <inheritdoc />
    public override bool Handles(string commandId) => string.Equals(commandId, Id, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Execute(string commandId, ICommandContext context) => Run(context);
}

/// <summary>
/// A set of commands generated from live state — one per character, window, pane or settings screen — whose
/// ids all start with <see cref="IdPrefix"/>. <see cref="IContribution.Id"/> names the family itself and is
/// never a command id.
/// </summary>
public sealed class CommandFamily : CommandContribution
{
    /// <summary>The prefix every id this family emits starts with, and so every id it runs.</summary>
    public required string IdPrefix { get; init; }

    /// <summary>Generates the entries from the current state.</summary>
    public required Func<ICommandContext, IEnumerable<CommandItem>> Generate { get; init; }

    /// <summary>Runs one of the family's ids.</summary>
    public required Func<string, ICommandContext, bool> Run { get; init; }

    /// <inheritdoc />
    public override IEnumerable<CommandItem> Entries(ICommandContext context) => Generate(context);

    /// <inheritdoc />
    public override bool Handles(string commandId) => commandId.StartsWith(IdPrefix, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Execute(string commandId, ICommandContext context) => Run(commandId, context);
}

/// <summary>
/// The registry's commands: builds the catalog the surface lists, and finds the contribution that runs an id.
/// A single command's id may not fall under a family's prefix, and one family's prefix may not contain
/// another's, so every id has exactly one contribution that runs it.
/// </summary>
public sealed class CommandList : ContributionList<CommandContribution>
{
    internal CommandList()
        : base(ContributionKind.Command)
    {
    }

    /// <summary>Every entry every contribution offers right now, in registration order.</summary>
    public IReadOnlyList<CommandItem> Catalog(ICommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var items = new List<CommandItem>();
        foreach (var contribution in this)
        {
            items.AddRange(contribution.Entries(context));
        }

        return items;
    }

    /// <summary>The contribution that runs <paramref name="commandId"/>: an exact single command first, else the family whose prefix it has.</summary>
    public CommandContribution? Resolve(string commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        if (Find(commandId) is SingleCommand single)
        {
            return single;
        }

        return this.OfType<CommandFamily>().FirstOrDefault(f => f.Handles(commandId));
    }

    /// <summary>
    /// Runs <paramref name="commandId"/> through the contribution that handles it. Returns false, and sets
    /// <paramref name="handled"/> false, when nothing does.
    /// </summary>
    public bool TryExecute(string commandId, ICommandContext context, out bool handled)
    {
        var contribution = Resolve(commandId);
        handled = contribution is not null;
        return contribution is not null && contribution.Execute(commandId, context);
    }

    /// <inheritdoc />
    protected override void Validate(CommandContribution contribution)
    {
        switch (contribution)
        {
            case SingleCommand single:
                ArgumentException.ThrowIfNullOrWhiteSpace(single.Title);
                if (this.OfType<CommandFamily>().FirstOrDefault(f => f.Handles(single.Id)) is { } owning)
                {
                    throw new ArgumentException(
                        $"the command id '{single.Id}' falls under the family '{owning.Id}' ({owning.IdPrefix})",
                        nameof(contribution));
                }

                break;
            case CommandFamily family:
                ArgumentException.ThrowIfNullOrWhiteSpace(family.IdPrefix);
                foreach (var other in this)
                {
                    var clash = other switch
                    {
                        SingleCommand s => family.Handles(s.Id),
                        CommandFamily f => f.IdPrefix.StartsWith(family.IdPrefix, StringComparison.Ordinal)
                            || family.IdPrefix.StartsWith(f.IdPrefix, StringComparison.Ordinal),
                        _ => false,
                    };
                    if (clash)
                    {
                        throw new ArgumentException(
                            $"the family '{family.Id}' ({family.IdPrefix}) overlaps '{other.Id}'",
                            nameof(contribution));
                    }
                }

                break;
        }
    }
}
