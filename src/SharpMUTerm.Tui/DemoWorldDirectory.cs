namespace SharpMUTerm.Tui;

/// <summary>
/// The demo scene's world directory: a mu*index listing and the pages it names, in mu*index's own JSON shapes
/// (<c>GameListView</c>, <c>GameView</c>, camelCase), answered from memory. Every world here is made up and every host is
/// under <c>.example</c> (RFC 2606), so a demo that picks one and presses ⏎ dials nothing real.
/// </summary>
internal static class DemoWorldDirectory
{
    private sealed record Game(string Id, string Name, string Tagline, string? Codebase, int? Players, string Count, string Host, int Port, bool Tls);

    private static readonly Game[] Games =
    [
        new("6f1c2a52-8d2e-4a52-9b8e-0c6a1f3d2e01", "Aetherfall", "Sky-islands roleplay under a shattered moon", "PennMUSH", 64, "measured", "aetherfall.example", 4201, true),
        new("6f1c2a52-8d2e-4a52-9b8e-0c6a1f3d2e02", "Lanternport", "A harbour city of guilds, smugglers and tide-bells", "TinyMUX", 41, "measured", "lanternport.example", 2860, false),
        new("6f1c2a52-8d2e-4a52-9b8e-0c6a1f3d2e03", "The Hollow Crown", "Courtly intrigue in a kingdom with an empty throne", "RhostMUSH", 33, "declared", "hollowcrown.example", 4000, false),
        new("6f1c2a52-8d2e-4a52-9b8e-0c6a1f3d2e04", "Ironvale", "Hack-and-slash with a hundred-zone map and remorts", "CircleMUD", 28, "measured", "ironvale.example", 4000, false),
        new("6f1c2a52-8d2e-4a52-9b8e-0c6a1f3d2e05", "Starfall Station", "Science-fiction roleplay aboard a failing waystation", "Evennia", 19, "measured", "starfall.example", 4000, true),
        new("6f1c2a52-8d2e-4a52-9b8e-0c6a1f3d2e06", "Mossgrove", "A slow, kind game about tending a forest village", "LambdaMOO", 12, "measured", "mossgrove.example", 7777, false),
        new("6f1c2a52-8d2e-4a52-9b8e-0c6a1f3d2e07", "Duskwatch", "Gothic horror; consent-based, plot-driven", "PennMUSH", 9, "declared", "duskwatch.example", 4201, false),
        new("6f1c2a52-8d2e-4a52-9b8e-0c6a1f3d2e08", "Copperline", "Steam, rails and frontier towns", "TinyMUX", null, "unknown", "copperline.example", 2000, false),
        new("6f1c2a52-8d2e-4a52-9b8e-0c6a1f3d2e09", "Glass Archipelago", "Islands, sails and a sea that remembers", null, null, "unknown", "glassarch.example", 4444, false),
    ];

    /// <summary>The answer to <paramref name="uri"/>: one game's page, or the listing (narrowed by its <c>q</c>).</summary>
    public static string Answer(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var path = uri.AbsolutePath.TrimEnd('/');
        var slash = path.LastIndexOf('/');
        if (slash >= 0 && Guid.TryParse(path[(slash + 1)..], out var id)
            && Games.FirstOrDefault(g => Guid.Parse(g.Id) == id) is { } game)
        {
            return Page(game);
        }

        var q = System.Web.HttpUtility.ParseQueryString(uri.Query)["q"] ?? string.Empty;
        var listed = Games.Where(g => q.Length == 0
            || g.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || g.Tagline.Contains(q, StringComparison.OrdinalIgnoreCase)
            || (g.Codebase ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase)).ToArray();
        var rows = string.Join(",", listed.Select(Summary));
        return $$"""{"apiVersion":"1","generatedAt":"2026-11-04T12:00:00+00:00","filter":{"q":{{Json(q.Length == 0 ? null : q)}}},"facets":[],"total":{{listed.Length}},"limit":200,"offset":0,"count":{{listed.Length}},"games":[{{rows}}]}""";
    }

    private static string Summary(Game g) =>
        $$"""{"id":"{{g.Id}}","slug":"{{g.Name.ToLowerInvariant().Replace(' ', '-')}}","name":{{Json(g.Name)}},"tagline":{{Json(g.Tagline)}},"state":"active","archived":false,"claimed":false,"playersNow":{{(g.Players is { } p ? p.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}},"playersNowState":"{{g.Count}}","playersNowProvenance":null,"codebase":{{Json(g.Codebase)}},"codebaseProvenance":null,"measuredProtocols":["gmcp","mssp"],"lastReachableAt":"2026-11-04T11:40:00+00:00","url":"/g/{{g.Id}}","apiUrl":"/api/games/{{g.Id}}"}""";

    private static string Page(Game g)
    {
        var endpoints = g.Tls
            ? $$"""[{"host":"{{g.Host}}","port":{{g.Port + 1}},"kind":"tls","tlsMeasured":true,"firstSeenAt":"2025-02-01T00:00:00+00:00","lastSeenAt":"2026-11-04T11:40:00+00:00","state":"active"},{"host":"{{g.Host}}","port":{{g.Port}},"kind":"telnet","tlsMeasured":false,"firstSeenAt":"2024-01-01T00:00:00+00:00","lastSeenAt":"2026-11-04T11:00:00+00:00","state":"active"}]"""
            : $$"""[{"host":"old.{{g.Host}}","port":{{g.Port}},"kind":"telnet","tlsMeasured":false,"firstSeenAt":"2019-01-01T00:00:00+00:00","lastSeenAt":"2021-06-01T00:00:00+00:00","state":"gone"},{"host":"{{g.Host}}","port":{{g.Port}},"kind":"telnet","tlsMeasured":false,"firstSeenAt":"2021-06-01T00:00:00+00:00","lastSeenAt":"2026-11-04T11:40:00+00:00","state":"active"}]""";
        return $$"""{"id":"{{g.Id}}","slug":"x","name":{{Json(g.Name)}},"tagline":{{Json(g.Tagline)}},"description":null,"state":"active","archived":false,"claimed":false,"playersNow":null,"playersNowState":"unknown","codebase":{{Json(g.Codebase)}},"measuredProtocols":[],"endpoints":{{endpoints}},"url":"/g/x","apiUrl":"/api/games/{{g.Id}}"}""";
    }

    private static string Json(string? text) => text is null ? "null" : System.Text.Json.JsonSerializer.Serialize(text);
}
