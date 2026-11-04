namespace SharpMUTerm.Core.Session;

/// <summary>
/// One thing a session says about its own connection (<see cref="WorldSession.ConnectionNoted"/>): a short label in
/// capitals and the words. The client draws it as a row of its own, not as a line of the game's output.
/// </summary>
/// <param name="Label">What happened: <see cref="Connect"/>, <see cref="Failed"/>, <see cref="Lost"/> or <see cref="Closed"/>.</param>
/// <param name="Text">The words: the host, and the reason when there is one.</param>
public sealed record ConnectionNote(string Label, string Text)
{
    /// <summary>Dialling, and connected.</summary>
    public const string Connect = "CONNECT";

    /// <summary>A dial that did not open.</summary>
    public const string Failed = "FAILED";

    /// <summary>An open connection that dropped.</summary>
    public const string Lost = "LOST";

    /// <summary>An open connection closed cleanly, by either end.</summary>
    public const string Closed = "CLOSED";

    /// <summary>The note as one plain line, for a reader with no rows (a screen reader, <c>--plain</c>, a test).</summary>
    public override string ToString() => $"{Label}: {Text}";
}
