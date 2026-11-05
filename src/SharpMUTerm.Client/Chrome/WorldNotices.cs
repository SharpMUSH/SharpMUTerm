using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Client.Chrome;

/// <summary>
/// The words of the notices about a world the client adds (the welcome's form, <c>/connect</c>, a <c>telnet://</c> link),
/// one sentence for both hosts: the status row draws it and the window's toast draws the same model.
/// </summary>
public static class WorldNotices
{
    /// <summary>
    /// A world added and dialled: its name (the endpoint is on the CONNECT rows just above, and a host name made the row
    /// too long for 60 columns), the encoding it was saved with when that is not <c>auto</c> (a
    /// charset mu*index named), and F5 named once, as the way to change any of it. The notice carries no key chip, so
    /// the row and the toast both say F5 once.
    /// </summary>
    public static string Added(string name, string host, int port, string? encoding)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(host);
        var saved = string.IsNullOrWhiteSpace(encoding)
            || encoding.Trim().Equals(TelnetSessionOptions.AutoEncodingName, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $" ({encoding.Trim()})";
        return $"added {name}{saved} — F5 edits it";
    }

    /// <summary>The added notice's action label: F5's worlds screen, which the row names by its key only when it fits.</summary>
    public const string EditAction = "Edit world";
}
