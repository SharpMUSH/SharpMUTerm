using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Tests.Configuration;

/// <summary>
/// Passwords in the OS credential store with <c>secrets.json</c> behind it. Every test uses <see cref="FakeStore"/>:
/// nothing here reaches a real keychain, and the files are in a throwaway folder.
/// </summary>
public class CredentialVaultTests
{
    /// <summary>A credential store in memory that can be told to stop answering or to refuse writes.</summary>
    internal sealed class FakeStore : ICredentialStore
    {
        public Dictionary<Guid, string> Items { get; } = [];

        public bool Reachable { get; set; } = true;

        public bool RefusesWrites { get; set; }

        public int Writes { get; private set; }

        public string Name => "the fake store";

        public CredentialLookup TryRead(Guid id, out string? password)
        {
            password = null;
            if (!Reachable)
            {
                return CredentialLookup.Unavailable;
            }

            return Items.TryGetValue(id, out password) ? CredentialLookup.Found : CredentialLookup.Missing;
        }

        public bool Write(Guid id, string password)
        {
            Writes++;
            if (!Reachable || RefusesWrites)
            {
                return false;
            }

            Items[id] = password;
            return true;
        }

        public bool Delete(Guid id)
        {
            if (!Reachable)
            {
                return false;
            }

            Items.Remove(id);
            return true;
        }
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Directory = Path.Combine(Path.GetTempPath(), $"smuterm-vault-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Directory);
        }

        public string Directory { get; }

        public string ConfigPath => Path.Combine(Directory, "SharpMUTerm", "config.json");

        public string SecretsPath => Path.Combine(Directory, "SharpMUTerm", SecretsStore.FileName);

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }

    private static AppConfiguration WithPasswords(params string?[] passwords)
    {
        var world = new WorldDefinition { Name = "Aetherfall", Host = "aetherfall.mux", Port = 4201 };
        for (var i = 0; i < passwords.Length; i++)
        {
            world.Characters.Add(new CharacterDefinition { Name = $"Corvid{i}", Password = passwords[i] });
        }

        var config = new AppConfiguration();
        config.Worlds.Add(world);
        return config;
    }

    private static CharacterDefinition First(AppConfiguration config) => config.Worlds[0].Characters[0];

    [Test]
    public async Task WithAStoreThePasswordGoesToItAndNotToTheFile()
    {
        using var temp = new TempRoot();
        var store = new FakeStore();
        var config = WithPasswords("hunter2");

        ConfigurationStore.Save(temp.ConfigPath, config, new CredentialVault(store));

        var id = First(config).PasswordRef!.Value;
        await Assert.That(store.Items[id]).IsEqualTo("hunter2");
        await Assert.That(File.Exists(temp.SecretsPath)).IsFalse();
        await Assert.That(File.ReadAllText(temp.ConfigPath)).DoesNotContain("hunter2");

        var reloaded = ConfigurationStore.Load(temp.ConfigPath, vault: new CredentialVault(store));
        await Assert.That(First(reloaded).Password).IsEqualTo("hunter2");
        await Assert.That(First(reloaded).PasswordRef).IsEqualTo(id);
    }

    [Test]
    public async Task ASaveThatChangedNothingWritesNothingToTheStore()
    {
        using var temp = new TempRoot();
        var store = new FakeStore();
        var vault = new CredentialVault(store);
        var config = WithPasswords("hunter2", "swordfish");

        ConfigurationStore.Save(temp.ConfigPath, config, vault);
        var afterFirst = store.Writes;
        ConfigurationStore.Save(temp.ConfigPath, config, vault);
        ConfigurationStore.Save(temp.ConfigPath, config, vault);

        await Assert.That(afterFirst).IsEqualTo(2);
        await Assert.That(store.Writes).IsEqualTo(2);
    }

    [Test]
    public async Task SecretsJsonEntriesMoveIntoTheStoreAtLoad()
    {
        using var temp = new TempRoot();
        var config = WithPasswords("hunter2", "swordfish");
        ConfigurationStore.Save(temp.ConfigPath, config); // the old way: secrets.json
        await Assert.That(File.Exists(temp.SecretsPath)).IsTrue();

        var store = new FakeStore();
        var notes = new List<string>();
        var loaded = ConfigurationStore.Load(temp.ConfigPath, notes.Add, new CredentialVault(store));

        await Assert.That(loaded.Worlds[0].Characters.Select(c => c.Password ?? "")).IsEquivalentTo(new[] { "hunter2", "swordfish" });
        await Assert.That(store.Items.Values).IsEquivalentTo(new[] { "hunter2", "swordfish" });
        await Assert.That(File.Exists(temp.SecretsPath)).IsFalse();
        await Assert.That(notes.Single()).Contains("moved 2 passwords");
    }

    [Test]
    public async Task APasswordTheStoreRefusesStaysInTheFile()
    {
        using var temp = new TempRoot();
        ConfigurationStore.Save(temp.ConfigPath, WithPasswords("hunter2"));
        var store = new FakeStore { RefusesWrites = true };

        var loaded = ConfigurationStore.Load(temp.ConfigPath, vault: new CredentialVault(store));

        await Assert.That(First(loaded).Password).IsEqualTo("hunter2");
        await Assert.That(SecretsStore.Read(temp.SecretsPath).Secrets.Values.Single()).IsEqualTo("hunter2");
    }

    [Test]
    public async Task WithNoStoreReachableThePasswordGoesToTheFile()
    {
        using var temp = new TempRoot();
        var store = new FakeStore { Reachable = false };
        var config = WithPasswords("hunter2");

        ConfigurationStore.Save(temp.ConfigPath, config, new CredentialVault(store));

        await Assert.That(store.Items).IsEmpty();
        await Assert.That(SecretsStore.Read(temp.SecretsPath).Secrets[First(config).PasswordRef!.Value]).IsEqualTo("hunter2");
    }

    /// <summary>
    /// The headless-Linux case: the passwords are in a keyring this run cannot reach. The characters start with no
    /// password — and the first save must not forget the references, or the next run with a keyring finds nothing.
    /// </summary>
    [Test]
    public async Task AStoreThatCannotBeReachedKeepsEveryReferenceThroughASave()
    {
        using var temp = new TempRoot();
        var store = new FakeStore();
        var config = WithPasswords("hunter2");
        ConfigurationStore.Save(temp.ConfigPath, config, new CredentialVault(store));
        var id = First(config).PasswordRef!.Value;

        store.Reachable = false;
        var notes = new List<string>();
        var offline = new CredentialVault(store);
        var loaded = ConfigurationStore.Load(temp.ConfigPath, notes.Add, offline);
        await Assert.That(First(loaded).Password).IsNull();
        await Assert.That(notes.Single()).Contains("could not be reached");

        ConfigurationStore.Save(temp.ConfigPath, loaded, offline);
        await Assert.That(First(loaded).PasswordRef).IsEqualTo(id);

        store.Reachable = true;
        var back = ConfigurationStore.Load(temp.ConfigPath, vault: new CredentialVault(store));
        await Assert.That(First(back).Password).IsEqualTo("hunter2");
    }

    [Test]
    public async Task ClearingAPasswordOrDeletingACharacterRemovesItFromTheStore()
    {
        using var temp = new TempRoot();
        var store = new FakeStore();
        var vault = new CredentialVault(store);
        var config = WithPasswords("hunter2", "swordfish");
        ConfigurationStore.Save(temp.ConfigPath, config, vault);
        await Assert.That(store.Items.Count).IsEqualTo(2);

        First(config).Password = null;
        config.Worlds[0].Characters.RemoveAt(1);
        ConfigurationStore.Save(temp.ConfigPath, config, vault);

        await Assert.That(store.Items).IsEmpty();
        await Assert.That(First(config).PasswordRef).IsNull();
    }

    [Test]
    public async Task ADeletedCharacterFromAnEarlierRunIsRemovedFromTheStore()
    {
        using var temp = new TempRoot();
        var store = new FakeStore();
        ConfigurationStore.Save(temp.ConfigPath, WithPasswords("hunter2"), new CredentialVault(store));

        var vault = new CredentialVault(store);
        var loaded = ConfigurationStore.Load(temp.ConfigPath, vault: vault);
        loaded.Worlds[0].Characters.Clear();
        ConfigurationStore.Save(temp.ConfigPath, loaded, vault);

        await Assert.That(store.Items).IsEmpty();
    }

    /// <summary>
    /// A changed password the store refuses goes to the file — under a new reference, because the store, read first,
    /// still holds the old one under the old reference and would win.
    /// </summary>
    [Test]
    public async Task AChangedPasswordTheStoreRefusesIsNotShadowedByTheOldOne()
    {
        using var temp = new TempRoot();
        var store = new FakeStore();
        var vault = new CredentialVault(store);
        var config = WithPasswords("old");
        ConfigurationStore.Save(temp.ConfigPath, config, vault);

        store.RefusesWrites = true;
        First(config).Password = "new";
        ConfigurationStore.Save(temp.ConfigPath, config, vault);

        var reloaded = ConfigurationStore.Load(temp.ConfigPath, vault: new CredentialVault(store));
        await Assert.That(First(reloaded).Password).IsEqualTo("new");
        await Assert.That(store.Items.Values).DoesNotContain("old");
    }

    [Test]
    public async Task AVaultWithNoStoreIsSecretsJsonAlone()
    {
        using var temp = new TempRoot();
        var config = WithPasswords("hunter2");

        ConfigurationStore.Save(temp.ConfigPath, config, new CredentialVault(null));
        var reloaded = ConfigurationStore.Load(temp.ConfigPath, vault: new CredentialVault(null));

        await Assert.That(SecretsStore.Read(temp.SecretsPath).Secrets.Values.Single()).IsEqualTo("hunter2");
        await Assert.That(First(reloaded).Password).IsEqualTo("hunter2");
    }
}
