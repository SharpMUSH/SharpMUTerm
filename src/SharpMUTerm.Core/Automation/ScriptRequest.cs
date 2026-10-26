using System.Text.RegularExpressions;

namespace SharpMUTerm.Core.Automation;

/// <summary>Which kind of rule asked for a script callback.</summary>
public enum ScriptSource
{
    Trigger,
    Alias,
    Timer,
    Macro,
}

/// <summary>
/// A rule asking the scripting layer to run its callback: the callback's name, the text that matched and
/// its capture groups. Core only reports the request; it never runs Lua.
/// </summary>
public sealed record ScriptRequest(ScriptSource Source, string Callback, string WholeMatch, IReadOnlyList<string> Groups)
{
    /// <summary>A request carrying a regex match's whole text and its numbered groups (group 0 excluded).</summary>
    public static ScriptRequest FromMatch(ScriptSource source, string callback, Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var groups = new string[Math.Max(0, match.Groups.Count - 1)];
        for (var i = 1; i < match.Groups.Count; i++)
        {
            groups[i - 1] = match.Groups[i].Value;
        }

        return new ScriptRequest(source, callback, match.Value, groups);
    }

    /// <summary>A request with nothing matched, for a timer firing or a key being pressed.</summary>
    public static ScriptRequest Bare(ScriptSource source, string callback) =>
        new(source, callback, string.Empty, Array.Empty<string>());
}
