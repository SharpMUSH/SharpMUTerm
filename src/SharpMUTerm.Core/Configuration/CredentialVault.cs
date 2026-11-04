namespace SharpMUTerm.Core.Configuration;

/// <summary>
/// Where character passwords are kept: the OS credential store when the machine has one that answers, and
/// <c>secrets.json</c> (<see cref="SecretsStore"/>) for any password the store would not take. The configuration still
/// carries only <see cref="CharacterDefinition.PasswordRef"/>, the same GUID in both places, so a password moves
/// between them without <c>config.json</c> changing.
/// <para>
/// <b>One vault per run</b>, handed to <see cref="ConfigurationStore.Load(string, Action{string}?, CredentialVault?)"/>
/// and every <see cref="ConfigurationStore.Save(string, AppConfiguration, CredentialVault?)"/> after it, because it
/// remembers what the run has learned: which references the store holds and with what (so a save that changed nothing
/// writes nothing to the store — saves happen on every committed settings edit, and a store write can be a process),
/// and which references the store could not be asked about.
/// </para>
/// <para>
/// <b>A password the store could not be asked about is not a password the player removed.</b> A Linux session with no
/// Secret Service running (over ssh, say) loads every stored character with no password. The reference is kept, and a
/// save keeps it, so the next run with a store reads the password again. Without that, the first save of such a run
/// would forget every reference and with it every password.
/// </para>
/// <para>
/// <b>Moving in.</b> A password found in <c>secrets.json</c> while the store answers is written to the store at load,
/// and the file is rewritten without it once the store has it. A password the store refuses stays in the file.
/// </para>
/// <para>
/// A vault with no store is <c>secrets.json</c> alone, exactly the behaviour from before there was a store; that is
/// what a test, a snapshot and a machine with no store use.
/// </para>
/// </summary>
public sealed class CredentialVault
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, string> _inStore = [];
    private readonly HashSet<Guid> _unresolved = [];

    /// <param name="store">The machine's store, or null for <c>secrets.json</c> alone.</param>
    public CredentialVault(ICredentialStore? store) => Store = store;

    /// <summary>The OS store, or null.</summary>
    public ICredentialStore? Store { get; }

    /// <summary>The references whose password is in the store, for the tests.</summary>
    internal IReadOnlyCollection<Guid> InStore
    {
        get
        {
            lock (_gate)
            {
                return [.. _inStore.Keys];
            }
        }
    }

    /// <summary>
    /// Fills in each character's password: from the store first, then from the file. Moves what the file holds into
    /// the store when the store takes it, and rewrites the file without it.
    /// </summary>
    internal void Load(AppConfiguration configuration, SecretsStore.ReadResult file, string secretsPath, Action<string>? report)
    {
        lock (_gate)
        {
            var migrated = new HashSet<Guid>();
            var unreachable = 0;
            foreach (var character in configuration.Worlds.SelectMany(w => w.Characters))
            {
                character.Password = null;
                if (character.PasswordRef is not { } id)
                {
                    continue;
                }

                var lookup = CredentialLookup.Missing;
                if (Store is not null)
                {
                    lookup = Store.TryRead(id, out var stored);
                    if (lookup == CredentialLookup.Found && !string.IsNullOrEmpty(stored))
                    {
                        character.Password = stored;
                        _inStore[id] = stored;
                        continue;
                    }
                }

                if (file.Secrets.TryGetValue(id, out var password))
                {
                    character.Password = password;
                    if (Store is not null && lookup == CredentialLookup.Missing && file.Readable && Store.Write(id, password))
                    {
                        _inStore[id] = password;
                        migrated.Add(id);
                    }

                    continue;
                }

                if (lookup == CredentialLookup.Unavailable)
                {
                    _unresolved.Add(id);
                    unreachable++;
                }
            }

            if (migrated.Count > 0)
            {
                var kept = file.Secrets.Where(entry => !migrated.Contains(entry.Key)).ToDictionary(e => e.Key, e => e.Value);
                try
                {
                    SecretsStore.Write(secretsPath, kept);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Both places hold the password now; the store is read first, and the next save prunes the file.
                }

                report?.Invoke($"moved {migrated.Count} password{(migrated.Count == 1 ? "" : "s")} from {SecretsStore.FileName} into {Store!.Name}");
            }

            if (unreachable > 0)
            {
                report?.Invoke(
                    $"{Store!.Name} could not be reached, so {unreachable} character{(unreachable == 1 ? " starts" : "s start")} " +
                    "with no password this run; they are kept and read again next time");
            }
        }
    }

    /// <summary>
    /// Brings every reference into line with the password its character holds, writes to the store what changed, and
    /// hands back what <c>secrets.json</c> must hold: the passwords the store did not take.
    /// <para>
    /// It <b>mutates the configuration it is given</b>, as the save always has: the GUID has to outlive the call, or
    /// every save would mint a new one and orphan the last.
    /// </para>
    /// </summary>
    internal Dictionary<Guid, string> Reconcile(AppConfiguration configuration)
    {
        lock (_gate)
        {
            var file = new Dictionary<Guid, string>();
            var referenced = new HashSet<Guid>();
            foreach (var character in configuration.Worlds.SelectMany(w => w.Characters))
            {
                if (string.IsNullOrEmpty(character.Password))
                {
                    // A password the store could not be asked about this run is kept, reference and all.
                    if (character.PasswordRef is { } kept && _unresolved.Contains(kept) && referenced.Add(kept))
                    {
                        continue;
                    }

                    // No password, no reference. A GUID pointing at nothing would say "this character has a stored
                    // password" to anyone reading the config, and it would keep a row alive.
                    character.PasswordRef = null;
                    continue;
                }

                // Two characters on one row — a hand-edited config, or one copied between machines — would mean
                // editing one password silently changed the other. Give this one a row of its own.
                var id = character.PasswordRef ?? Guid.NewGuid();
                if (!referenced.Add(id))
                {
                    id = Guid.NewGuid();
                    referenced.Add(id);
                }

                _unresolved.Remove(id);
                if (Store is not null)
                {
                    if (_inStore.TryGetValue(id, out var known) && known == character.Password)
                    {
                        character.PasswordRef = id;
                        continue;
                    }

                    if (Store.Write(id, character.Password))
                    {
                        _inStore[id] = character.Password;
                        character.PasswordRef = id;
                        continue;
                    }

                    // The store holds an older password under this reference and refused the new one. Read first, it
                    // would win over the file, so the new password goes to the file under a reference of its own.
                    if (_inStore.Remove(id))
                    {
                        Store.Delete(id);
                        referenced.Remove(id);
                        id = Guid.NewGuid();
                        referenced.Add(id);
                    }
                }

                file[id] = character.Password;
                character.PasswordRef = id;
            }

            // What the store holds for a character that is gone, or whose password moved to the file, goes too.
            foreach (var stale in _inStore.Keys.Where(id => !referenced.Contains(id) || file.ContainsKey(id)).ToList())
            {
                if (Store?.Delete(stale) ?? true)
                {
                    _inStore.Remove(stale);
                }
            }

            _unresolved.IntersectWith(referenced);
            return file;
        }
    }
}
