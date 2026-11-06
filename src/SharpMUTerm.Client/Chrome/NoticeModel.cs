namespace SharpMUTerm.Client.Chrome;

/// <summary>How loud a notice is. Each host says it in its own way: the status row by ink, a toast by its edge.</summary>
public enum NoticeSeverity
{
    /// <summary>Something happened that the player asked for (a character switched, a layout applied).</summary>
    Info,

    /// <summary>Something failed: a connection that would not open, a settings file that would not save.</summary>
    Warning,

    /// <summary>The client declined to do what was asked, or did less than the label promised (the default).</summary>
    Refusal,
}

/// <summary>A command a notice offers, by registry id, with what a host may call it.</summary>
/// <param name="Command">The command id; run through the client's registry, never built by a host.</param>
/// <param name="Label">What the action does, for a button or a tooltip (<c>Reconnect</c>).</param>
public sealed record NoticeAction(string Command, string Label);

/// <summary>
/// One notice, as data: the client's <c>display-message</c>, said once and gone in a few seconds. The terminal draws
/// it on the status row (<c>SharpMUTermApp.NoticeMarkup</c>); the window draws the same instance as a toast over the
/// cells. Every notice is kept in the client message log as well (⌃P ▸ Show client messages), which is the history:
/// neither view keeps one.
/// </summary>
/// <param name="Text">What it says, as plain text.</param>
/// <param name="Severity">How loud it is.</param>
/// <param name="Key">An optional chip naming the surface that spoke (<c>⌃B</c>, <c>⌃P</c>, <c>⌥M</c>).</param>
/// <param name="Action">A command the notice offers, or null. Only the window can offer it as a click.</param>
/// <param name="At">When it was said, on the client's clock.</param>
/// <param name="Sequence">Counts the run's notices, so two that say the same thing are still two.</param>
/// <param name="Source">
/// What part of the client spoke when it is not a key (<c>Packages</c>, <c>Map</c>, <c>Sound</c>): a label, drawn as one —
/// <c>Sound:</c> on the status row, a plain caption on a toast — never in the key chip, where a word read as a chord to
/// press. A notice carries a <paramref name="Key"/> or a source, not both.
/// </param>
public sealed record NoticeModel(string Text, NoticeSeverity Severity, string? Key, NoticeAction? Action, DateTimeOffset At, long Sequence, string? Source = null)
{
    /// <summary>The source labels in use, so every notice of one part of the client names it the same way.</summary>
    public static class Sources
    {
        /// <summary>Package installs, the index, removals.</summary>
        public const string Packages = "Packages";

        /// <summary>The map and a speedwalk.</summary>
        public const string Map = "Map";

        /// <summary>Game sound and mute.</summary>
        public const string Sound = "Sound";

        /// <summary>Spelling dictionary downloads.</summary>
        public const string Spelling = "Spelling";

        /// <summary>Imports from another client.</summary>
        public const string Import = "Import";

        /// <summary>Scene logs: recording, saved.</summary>
        public const string Scene = "Scene log";

        /// <summary>A newer release: found, downloading, ready to restart into.</summary>
        public const string Updates = "Updates";
    }

    /// <summary>
    /// A second command the notice offers, after <see cref="Action"/> (a saved scene's <em>Copy path</em> beside
    /// <em>Open</em>); null for none, and always null without a first. Only the window offers it, as its own click.
    /// </summary>
    public NoticeAction? Second { get; init; }

    /// <summary>How long a notice holds the status row, and how long a toast stays before it fades.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(6);
}
