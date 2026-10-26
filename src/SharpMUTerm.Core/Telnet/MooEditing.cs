using TelnetNegotiationCore.Models;

namespace SharpMUTerm.Core.Telnet;

/// <summary>
/// A buffer a MOO handed this client to edit over MCP (<c>dns-org-mud-moo-simpleedit</c>), as the server
/// sent it. Saving it sends the lines back under the same <see cref="Reference"/> and <see cref="Type"/>.
/// </summary>
/// <param name="Reference">The server's handle for what is being edited, such as <c>#98:look</c>. Opaque.</param>
/// <param name="Name">What to call it, such as <c>Test:look</c>; the reference when the server named none.</param>
/// <param name="Type"><c>moo-code</c>, <c>string-list</c> or <c>string</c>.</param>
/// <param name="Lines">The content, a line each.</param>
public sealed record MooEdit(string Reference, string Name, string Type, IReadOnlyList<string> Lines);

/// <summary>The MCP packages this client speaks, and their message names.</summary>
public static class MooEditing
{
    /// <summary>Every package here is 1.0.</summary>
    public static readonly McpVersion Version = new(1, 0);

    /// <summary>Editing a verb, a property or a note in the client.</summary>
    public const string SimpleEdit = "dns-org-mud-moo-simpleedit";

    /// <summary>Server to client: a buffer to edit.</summary>
    public const string SimpleEditContent = SimpleEdit + "-content";

    /// <summary>Client to server: the edited buffer.</summary>
    public const string SimpleEditSet = SimpleEdit + "-set";

    /// <summary>The client's name and version, and its screen size.</summary>
    public const string VmooClient = "dns-com-vmoo-client";

    /// <summary>Client to server: name and version.</summary>
    public const string VmooClientInfo = VmooClient + "-info";

    /// <summary>Client to server: columns and rows.</summary>
    public const string VmooClientScreenSize = VmooClient + "-screensize";

    /// <summary>Server to client: one line of status text. The package and its one message share the name.</summary>
    public const string AwnsStatus = "dns-com-awns-status";

    /// <summary>The simpleedit type for a list of lines, and what an edit with no type is read as.</summary>
    public const string StringList = "string-list";

    /// <summary>
    /// The lines of an edited buffer, as typed: CRLF, LF and a lone CR alike, and nothing trimmed but one
    /// newline at the very end, which is where the editor's caret was left. An empty buffer is no lines.
    /// </summary>
    public static IReadOnlyList<string> LinesOf(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return [];
        }

        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();
        if (lines.Count > 1 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}
