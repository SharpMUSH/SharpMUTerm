namespace SharpMUTerm.Core.Configuration;

/// <summary>What a read from an OS credential store found.</summary>
public enum CredentialLookup
{
    /// <summary>The store has a password under that reference.</summary>
    Found,

    /// <summary>The store answered, and has nothing under that reference.</summary>
    Missing,

    /// <summary>The store could not be asked: no service, locked and not unlocked, timed out, or failed.</summary>
    Unavailable,
}

/// <summary>
/// An operating system's credential store — Windows Credential Manager, the Secret Service (libsecret) on Linux, the
/// macOS Keychain — keyed by a character's <see cref="CharacterDefinition.PasswordRef"/>. The implementations are the
/// hosts' (<c>SharpMUTerm.Tui/Hosting</c>), because which store a machine has is a host fact; this side only says what
/// the configuration needs from one, so the reconciling in <see cref="CredentialVault"/> can be tested against a fake.
/// <para>
/// Every member must answer rather than throw, and must not block for long: a store that cannot be reached is
/// <see cref="CredentialLookup.Unavailable"/> and a write that fails is false, and the caller falls back to
/// <c>secrets.json</c> (<see cref="SecretsStore"/>) for that password.
/// </para>
/// </summary>
public interface ICredentialStore
{
    /// <summary>What the store is called, for a message: <c>Windows Credential Manager</c>.</summary>
    string Name { get; }

    /// <summary>Reads the password stored under <paramref name="id"/>.</summary>
    CredentialLookup TryRead(Guid id, out string? password);

    /// <summary>Stores <paramref name="password"/> under <paramref name="id"/>, replacing any. False when it was not stored.</summary>
    bool Write(Guid id, string password);

    /// <summary>Removes what is stored under <paramref name="id"/>. True when nothing is left there, removed or never present.</summary>
    bool Delete(Guid id);
}
