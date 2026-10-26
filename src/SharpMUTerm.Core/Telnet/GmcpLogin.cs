using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUTerm.Core.Telnet;

/// <summary>
/// GMCP's <c>Char.Login</c>, as Mudlet and the servers written for it speak it. The server says
/// <c>Char.Login.Default {"type": ["password-credentials"]}</c>, the client answers
/// <c>Char.Login.Credentials {"account": …, "password": …}</c>, and the server reports
/// <c>Char.Login.Result {"success": …, "message": …}</c>.
/// </summary>
/// <remarks>
/// The password is built into the message here and goes to <c>SendGmcpAsync</c> and nowhere else: not to the
/// transcript, the scrollback, the game-data view or the diagnostics log, which is the rule the connect line is
/// already held to.
/// </remarks>
public static class GmcpLogin
{
    /// <summary>What a client lists in <c>Core.Supports.Set</c> to say it can log in this way.</summary>
    public const string Module = "Char.Login 1";

    public const string DefaultPackage = "Char.Login.Default";
    public const string CredentialsPackage = "Char.Login.Credentials";
    public const string ResultPackage = "Char.Login.Result";

    /// <summary>The login type this client implements.</summary>
    public const string PasswordCredentials = "password-credentials";

    /// <summary>
    /// Whether a <c>Char.Login.Default</c> body offers <see cref="PasswordCredentials"/>. The <c>type</c> may be a
    /// list or a single string; anything else offers nothing.
    /// </summary>
    public static bool OffersCredentials(string json)
    {
        try
        {
            var type = JsonNode.Parse(json)?["type"];
            return type switch
            {
                JsonArray list => list.Any(t => IsCredentials(t)),
                JsonValue one => IsCredentials(one),
                _ => false,
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The <c>Char.Login.Credentials</c> body for a character.</summary>
    public static string Credentials(string account, string password) =>
        new JsonObject { ["account"] = account, ["password"] = password }.ToJsonString();

    /// <summary>A <c>Char.Login.Result</c> body, or null when it is not one.</summary>
    public static LoginResult? ReadResult(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject body || body["success"] is not JsonValue success)
            {
                return null;
            }

            var ok = success.TryGetValue(out bool flag) ? flag
                : success.TryGetValue(out string? text) && string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
            var message = body["message"] is JsonValue m && m.TryGetValue(out string? said) ? said : null;
            return new LoginResult(ok, message is null ? null : Bounded(message));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsCredentials(JsonNode? node) =>
        node is JsonValue value
        && value.TryGetValue(out string? type)
        && string.Equals(type, PasswordCredentials, StringComparison.OrdinalIgnoreCase);

    /// <summary>A server's message is printed in the window, so it is kept to one short line.</summary>
    private static string Bounded(string message)
    {
        var line = string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return line.Length <= 200 ? line : line[..199] + "…";
    }
}

/// <summary>How the server says a GMCP login went.</summary>
public sealed record LoginResult(bool Success, string? Message);
