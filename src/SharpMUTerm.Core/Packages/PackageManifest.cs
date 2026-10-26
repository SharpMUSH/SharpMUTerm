using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SharpMUTerm.Core.Packages;

/// <summary>
/// A package's <c>package.json</c>: who it is, which games it is for, and the client it needs. Read and
/// checked before anything else in the archive is opened, so a package that cannot say what it is never gets
/// as far as its rules.
/// </summary>
public sealed partial record PackageManifest
{
    /// <summary>Lower-case letters, digits, dots and dashes; the folder it installs into.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>SemVer: <c>1.2.3</c>, optionally with a <c>-pre</c> suffix.</summary>
    public required string Version { get; init; }

    public string? Author { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// The servers it is for: <c>host</c>, <c>host:port</c>, <c>*.domain</c>, or <c>*</c> for every game. Its
    /// sets apply to the characters of matching worlds and nobody else, so a package for one game cannot send
    /// commands into another.
    /// </summary>
    public List<string> Games { get; init; } = new();

    /// <summary>The oldest client version it works with, or null.</summary>
    public string? MinClient { get; init; }

    public const int MaxIdLength = 64;

    public const int MaxTextLength = 500;

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]*$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^(\*|(\*\.)?[A-Za-z0-9.-]+(:\d{1,5})?)$")]
    private static partial Regex GamePattern();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>The manifest in <paramref name="json"/>, or the reason it is not one.</summary>
    public static (PackageManifest? Manifest, string? Problem) Parse(string json)
    {
        PackageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PackageManifest>(json, Options);
        }
        catch (JsonException ex)
        {
            return (null, $"package.json does not parse: {ex.Message}");
        }

        if (manifest is null)
        {
            return (null, "package.json is empty");
        }

        return manifest.Problem() is { } problem ? (null, problem) : (manifest, null);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>What is wrong with this manifest, or null.</summary>
    public string? Problem()
    {
        if (Id.Length > MaxIdLength || !IdPattern().IsMatch(Id) || Id.Contains("..", StringComparison.Ordinal))
        {
            return $"the id '{Id}' is not lower-case letters, digits, dots and dashes";
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Length > MaxTextLength)
        {
            return "the package has no name";
        }

        if (!VersionPattern().IsMatch(Version))
        {
            return $"the version '{Version}' is not of the form 1.2.3";
        }

        if (Games.Count == 0)
        {
            return "the package names no games (use \"*\" for every game)";
        }

        if (Games.FirstOrDefault(g => !GamePattern().IsMatch(g)) is { } bad)
        {
            return $"'{bad}' is not a game address";
        }

        if ((Description?.Length ?? 0) > MaxTextLength || (Author?.Length ?? 0) > MaxTextLength)
        {
            return "the description or author is too long";
        }

        return null;
    }

    /// <summary>Whether this package is for the server at <paramref name="host"/>:<paramref name="port"/>.</summary>
    public bool IsFor(string host, int port) => Games.Any(g => Matches(g, host, port));

    public static bool Matches(string game, string host, int port)
    {
        if (game == "*")
        {
            return true;
        }

        var colon = game.LastIndexOf(':');
        var pattern = colon < 0 ? game : game[..colon];
        if (colon >= 0 && game[(colon + 1)..] != port.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            return false;
        }

        return pattern.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)
            : string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
    }
}
