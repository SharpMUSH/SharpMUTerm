using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Chrome;

/// <summary>
/// The one way a character is named to a person: <c>Corvid · Aetherfall</c>, or the world alone for a session with no
/// character. The dotted <c>World.Character</c> session key is an address, never a name — it is what the quit prompt,
/// ⌃P, ⌥M and the packages page used to print, each a little differently. Every surface in both hosts reads this.
/// </summary>
public static class CharacterNames
{
    /// <summary>What goes between the character and the world.</summary>
    public const string Separator = " · ";

    /// <summary><c>Corvid · Aetherfall</c>; the world alone when there is no character.</summary>
    public static string Display(string world, string? character)
    {
        ArgumentNullException.ThrowIfNull(world);
        return string.IsNullOrEmpty(character) ? world : character + Separator + world;
    }

    /// <summary>A rail character's name.</summary>
    public static string Display(CharacterRef character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return Display(character.WorldName, character.CharacterName);
    }

    /// <summary>
    /// The name for a session key, resolved against <paramref name="characters"/> (the configured characters, or the
    /// open sessions) so a world whose name holds a dot is still split where the key was joined. A key nothing names
    /// is split at its first dot, the way a key is built.
    /// </summary>
    public static string ForKey(string sessionKey, IEnumerable<CharacterRef>? characters)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        if (characters is not null)
        {
            foreach (var character in characters)
            {
                if (string.Equals(character.SessionKey, sessionKey, StringComparison.Ordinal))
                {
                    return Display(character);
                }
            }
        }

        var dot = sessionKey.IndexOf('.', StringComparison.Ordinal);
        return dot <= 0 || dot == sessionKey.Length - 1
            ? sessionKey
            : Display(sessionKey[..dot], sessionKey[(dot + 1)..]);
    }

    /// <summary>The name for a session key, resolved against a configuration's worlds and characters.</summary>
    public static string ForKey(string sessionKey, AppConfiguration? configuration) =>
        ForKey(sessionKey, configuration is null ? null : Refs(configuration));

    /// <summary>Every configured character as a <see cref="CharacterRef"/>, worlds alone included (no character).</summary>
    private static IEnumerable<CharacterRef> Refs(AppConfiguration configuration)
    {
        foreach (var world in configuration.Worlds.ToArray())
        {
            yield return new CharacterRef(world.Name, string.Empty, world.Name, false);
            foreach (var character in world.Characters.ToArray())
            {
                yield return new CharacterRef(world.Name, character.Name, $"{world.Name}.{character.Name}", false);
            }
        }
    }
}
