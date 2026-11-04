using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Import;

/// <summary>One world of an import, as it would land.</summary>
/// <param name="Name">The name it would have: the file's, or with a suffix when another world already has it.</param>
/// <param name="Host">Its host.</param>
/// <param name="Port">Its port.</param>
/// <param name="Existing">Whether a world with this host and port is already configured, so the import only adds characters to it.</param>
/// <param name="NewCharacters">The characters it would add.</param>
/// <param name="KeptCharacters">The characters the file names that the world already has, which are left as they are.</param>
public sealed record PlannedWorld(
    string Name,
    string Host,
    int Port,
    bool Existing,
    IReadOnlyList<string> NewCharacters,
    IReadOnlyList<string> KeptCharacters)
{
    public bool Equals(PlannedWorld? other) =>
        other is not null && Name == other.Name && Host == other.Host && Port == other.Port && Existing == other.Existing
        && NewCharacters.SequenceEqual(other.NewCharacters) && KeptCharacters.SequenceEqual(other.KeptCharacters);

    public override int GetHashCode() => HashCode.Combine(Name, Host, Port, Existing, NewCharacters.Count, KeptCharacters.Count);
}

/// <summary>
/// What an import would add to a configuration, decided against it before anything is written: <b>nothing is
/// overwritten</b>. The automation becomes a new set named <c>Imported from MUSHclient</c> (<c>(2)</c>, <c>(3)</c>… when
/// that name is taken); a world whose host and port are already configured gains only the characters it does not have;
/// any other world whose name is taken gets a suffix. The new set is listed on every character the import adds, and on
/// no character the player already had.
/// </summary>
public sealed class ImportPlan
{
    private ImportPlan(ImportResult result, IReadOnlyList<PlannedWorld> worlds, string? setName)
    {
        Result = result;
        Worlds = worlds;
        SetName = setName;
    }

    /// <summary>What the file held.</summary>
    public ImportResult Result { get; }

    /// <summary>The worlds, in the file's order.</summary>
    public IReadOnlyList<PlannedWorld> Worlds { get; }

    /// <summary>The new set's name, or null when the file held no automation.</summary>
    public string? SetName { get; }

    /// <summary>How many characters would be added.</summary>
    public int NewCharacterCount => Worlds.Sum(w => w.NewCharacters.Count);

    /// <summary>How many passwords the file holds for characters this import would add — the only ones it could store.</summary>
    public int PasswordCount => StorablePasswords().Count();

    /// <summary>Decides names for <paramref name="result"/> against <paramref name="configuration"/>. Writes nothing.</summary>
    public static ImportPlan For(AppConfiguration configuration, ImportResult result)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(result);
        var takenWorlds = new HashSet<string>(configuration.Worlds.Select(w => w.Name), StringComparer.OrdinalIgnoreCase);
        var worlds = new List<PlannedWorld>();
        foreach (var world in result.Worlds)
        {
            var existing = configuration.Worlds.FirstOrDefault(w =>
                string.Equals(w.Host, world.Host, StringComparison.OrdinalIgnoreCase) && w.Port == world.Port);
            var names = world.Characters.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (existing is not null)
            {
                var have = existing.Characters.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                worlds.Add(new PlannedWorld(
                    existing.Name,
                    existing.Host,
                    existing.Port,
                    Existing: true,
                    names.Where(n => !have.Contains(n)).ToArray(),
                    names.Where(have.Contains).ToArray()));
                continue;
            }

            var name = Unique(world.Name, takenWorlds);
            takenWorlds.Add(name);
            worlds.Add(new PlannedWorld(name, world.Host, world.Port, Existing: false, names, []));
        }

        string? setName = null;
        if (result.RuleCount > 0)
        {
            var takenSets = new HashSet<string>(configuration.TriggerSets.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
            setName = Unique("Imported from " + ImportFormats.Name(result.Format), takenSets);
        }

        return new ImportPlan(result, worlds, setName);
    }

    /// <summary>Whether two plans would write the same things — so a review shown is the import done.</summary>
    public bool SameAs(ImportPlan other) =>
        ReferenceEquals(Result, other.Result) && SetName == other.SetName && Worlds.SequenceEqual(other.Worlds);

    /// <summary>
    /// Adds what the plan says to <paramref name="configuration"/>, copying every definition so nothing the review
    /// holds is shared with the live configuration. A password is set only when <paramref name="storePasswords"/>,
    /// and only on a character this import adds; the caller's save then puts it where every password goes (the OS
    /// credential store, else <c>secrets.json</c>), never <c>config.json</c>.
    /// </summary>
    public ImportApplied Apply(AppConfiguration configuration, bool storePasswords)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!SameAs(For(configuration, Result)))
        {
            throw new InvalidOperationException("the configuration changed since this plan was made");
        }

        if (SetName is { } setName)
        {
            configuration.TriggerSets.Add(new TriggerSet
            {
                Name = setName,
                Description = $"Imported from {ImportFormats.Name(Result.Format)} ({Result.Source})",
                Triggers = Result.Set.Triggers.Select(t => t.Clone()).ToList(),
                Aliases = Result.Set.Aliases.Select(a => a.Clone()).ToList(),
                Timers = Result.Set.Timers.Select(CloneTimer).ToList(),
                Macros = Result.Set.Macros.Select(CloneMacro).ToList(),
            });
        }

        var passwords = storePasswords ? StorablePasswords().ToList() : [];
        var stored = 0;
        for (var i = 0; i < Worlds.Count; i++)
        {
            var plan = Worlds[i];
            var source = Result.Worlds[i];
            WorldDefinition target;
            if (plan.Existing)
            {
                target = configuration.Worlds.First(w =>
                    string.Equals(w.Host, plan.Host, StringComparison.OrdinalIgnoreCase) && w.Port == plan.Port);
            }
            else
            {
                target = new WorldDefinition
                {
                    Name = plan.Name,
                    Host = source.Host,
                    Port = source.Port,
                    UseTls = source.UseTls,
                };
                configuration.Worlds.Add(target);
            }

            foreach (var name in plan.NewCharacters)
            {
                var character = new CharacterDefinition { Name = name };
                if (SetName is not null)
                {
                    character.TriggerSets.Add(SetName);
                }

                if (passwords.FirstOrDefault(p => p.World == i && string.Equals(p.Character, name, StringComparison.OrdinalIgnoreCase)) is { } password)
                {
                    character.Password = password.Password;
                    stored++;
                }

                target.Characters.Add(character);
            }
        }

        return new ImportApplied(Worlds.Count(w => !w.Existing), NewCharacterCount, SetName, Result.RuleCount, stored);
    }

    private IEnumerable<ImportedPassword> StorablePasswords() =>
        Result.Passwords.Where(p => p.World >= 0 && p.World < Worlds.Count
            && Worlds[p.World].NewCharacters.Contains(p.Character, StringComparer.OrdinalIgnoreCase));

    private static string Unique(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name))
        {
            return name;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{name} ({n})";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static Automation.TimerDefinition CloneTimer(Automation.TimerDefinition timer) => new()
    {
        Name = timer.Name,
        IntervalSeconds = timer.IntervalSeconds,
        Command = timer.Command,
        OneShot = timer.OneShot,
        Enabled = timer.Enabled,
    };

    private static Automation.Macro CloneMacro(Automation.Macro macro) => new()
    {
        Name = macro.Name,
        Key = macro.Key,
        Command = macro.Command,
        Enabled = macro.Enabled,
    };
}

/// <summary>What an import added.</summary>
public sealed record ImportApplied(int Worlds, int Characters, string? SetName, int Rules, int PasswordsStored);
