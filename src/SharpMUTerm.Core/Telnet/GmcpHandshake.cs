using System.Globalization;
using System.Reflection;
using TelnetNegotiationCore.Gmcp;

namespace SharpMUTerm.Core.Telnet;

/// <summary>
/// What this client says once GMCP is agreed: <c>Core.Hello</c> with its name and version, then
/// <c>Core.Supports.Set</c> with the modules it wants. Many servers send nothing until the second arrives —
/// IRE games and every server built on their convention among them — and this client sent neither, so on
/// those servers GMCP was negotiated and then silent.
/// </summary>
/// <remarks>
/// Nothing here is sent unless the server offered GMCP (<c>IAC WILL GMCP</c>) and the library agreed to it:
/// the handshake rides <c>OnGMCPNegotiated</c>, so the rule that this client asks no server to enable an
/// option it has not offered still holds.
/// </remarks>
public static class GmcpHandshake
{
    /// <summary>The name this client introduces itself by.</summary>
    public const string ClientName = "SharpMUTerm";

    /// <summary>
    /// The modules asked for on every world, Mudlet's common set. A world adds to these on F5; it cannot
    /// remove them, because a server ignores a module it does not have.
    /// </summary>
    public static IReadOnlyList<string> DefaultModules { get; } =
        ["Char 1", "Char.Skills 1", "Char.Items 1", "Room 1", "Comm.Channel 1"];

    /// <summary>
    /// This build's version, as MinVer stamped it, without the commit suffix a server has no use for.
    /// </summary>
    public static string ClientVersion { get; } = ReadVersion();

    /// <summary>
    /// The modules for one world: the defaults, then the world's own, each parsed from <c>Name Version</c>
    /// (the version defaults to 1). A later entry for a module already listed replaces its version; an entry
    /// that does not parse is dropped.
    /// </summary>
    public static IReadOnlyList<GmcpModule> Modules(IEnumerable<string>? extra = null)
    {
        var modules = new List<GmcpModule>();
        foreach (var spec in DefaultModules.Concat(extra ?? []))
        {
            if (Parse(spec) is not { } module)
            {
                continue;
            }

            var at = modules.FindIndex(m => string.Equals(m.Name, module.Name, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                modules[at] = module;
            }
            else
            {
                modules.Add(module);
            }
        }

        return modules;
    }

    /// <summary>Parses <c>Char.Vitals 1</c> or <c>Char.Vitals</c>; null for anything else.</summary>
    public static GmcpModule? Parse(string? spec)
    {
        var parts = (spec ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is 0 or > 2 || !IsName(parts[0]))
        {
            return null;
        }

        var version = 1;
        if (parts.Length == 2
            && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out version) || version < 1))
        {
            return null;
        }

        return new GmcpModule(parts[0], version);
    }

    private static bool IsName(string name) =>
        name.Length is > 0 and <= 64
        && name[0] != '.' && name[^1] != '.'
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static string ReadVersion()
    {
        var info = typeof(GmcpHandshake).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info))
        {
            return "0.0.0";
        }

        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? info : info[..plus];
    }
}
