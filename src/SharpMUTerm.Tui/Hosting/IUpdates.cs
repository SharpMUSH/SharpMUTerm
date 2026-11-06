namespace SharpMUTerm.Tui;

/// <summary>
/// A host that can update the program it is running: find a newer release, download it, and put it in place once
/// the client has quit. Only an installed copy can (the Windows installer's or the AppImage's); a build run from a
/// folder, the snapshot, the tests and <c>--plain</c> have none. The client decides when to look and what to say; the
/// host only does the work.
/// </summary>
internal interface IUpdates
{
    /// <summary>The version running now, as the package says it (<c>0.2.0</c>).</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Whether applying an update starts this program again. False for the Windows terminal: the update is put in
    /// place once the console program has gone, and a console cannot be given back to a new one, so the player starts
    /// it again. <see cref="RestartNote"/> says so.
    /// </summary>
    bool Relaunches { get; }

    /// <summary>One sentence on what restarting to update does here, for the quit prompt. Null when there is nothing to add.</summary>
    string? RestartNote { get; }

    /// <summary>
    /// Asks the release feed for a version newer than this one: null when there is none. Prereleases only when
    /// <paramref name="prereleases"/>. Throws on a network or feed failure; the caller says it.
    /// </summary>
    Task<UpdateOffer?> CheckAsync(bool prereleases, CancellationToken cancel);

    /// <summary>Downloads <paramref name="offer"/>, reporting 0–100. Throws on failure.</summary>
    Task DownloadAsync(UpdateOffer offer, Action<int> progress, CancellationToken cancel);

    /// <summary>
    /// Called once the client's loop has ended and the configuration is saved: puts the downloaded
    /// <paramref name="offer"/> in place and, where <see cref="Relaunches"/>, starts the program again with the
    /// arguments this one was started with. Returns a line for the console, or null.
    /// </summary>
    string? ApplyAfterExit(UpdateOffer offer);
}

/// <summary>A newer release the feed named. <see cref="Handle"/> is the host's own record of it.</summary>
internal sealed record UpdateOffer(string Version, object Handle)
{
    /// <summary>The release's notes in Markdown, when the feed carries them.</summary>
    public string? Notes { get; init; }
}
