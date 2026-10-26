using System.Text.RegularExpressions;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Mapping;

/// <summary>
/// Rooms from the text a game prints, for games that send no room data: a line matching the world's
/// <see cref="MapSettings.RoomNamePattern"/> names a room, and the next line matching its
/// <see cref="MapSettings.ExitsPattern"/> lists the exits and completes it. Text has no room numbers, so the
/// observation carries none and the <see cref="MapRecorder"/> works out which room it is.
/// <para>
/// The patterns are read when the source is built; a bad one switches the source off rather than throwing,
/// and <see cref="Problem"/> says which. Matching has a timeout, because it runs on every line.
/// </para>
/// </summary>
public sealed class TextRoomSource : IRoomSource
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);
    private static readonly Regex ExitSeparator = new(@"\s*(?:,|\band\b|\s)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

    private readonly WorldSession _session;
    private readonly Regex? _name;
    private readonly Regex? _exits;
    private string? _pendingName;

    public TextRoomSource(WorldSession session, MapSettings settings)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(settings);
        _name = Compile(settings.RoomNamePattern, "room name");
        _exits = Compile(settings.ExitsPattern, "exits");
        if (_exits is not null)
        {
            _session.LinePrinted += OnLine;
        }
    }

    public event EventHandler<RoomObservation>? RoomEntered;

    /// <summary>Why a pattern was not used, or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Whether this source is listening at all: it needs at least the exits pattern.</summary>
    public bool IsActive => _exits is not null;

    public void Dispose() => _session.LinePrinted -= OnLine;

    private Regex? Compile(string? pattern, string what)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException e)
        {
            Problem = $"the map's {what} pattern is not a regular expression: {e.Message}";
            return null;
        }
    }

    private void OnLine(object? sender, StyledLine line) => Observe(line.Text);

    /// <summary>One line of output. Public so the matching can be driven without a session's plumbing.</summary>
    public void Observe(string text)
    {
        try
        {
            if (_name?.Match(text) is { Success: true } named)
            {
                _pendingName = (named.Groups["name"] is { Success: true } group ? group.Value : named.Value).Trim();
                return;
            }

            if (_exits?.Match(text) is not { Success: true } listed)
            {
                return;
            }

            var list = listed.Groups["exits"] is { Success: true } exits ? exits.Value : listed.Value;
            var observed = ExitSeparator.Split(list)
                .Select(e => e.Trim().Trim('.', '[', ']', '(', ')'))
                .Where(e => e.Length > 0 && !e.Equals("none", StringComparison.OrdinalIgnoreCase))
                .Select(e => new ObservedExit(MapDirections.Normalise(e) ?? e.ToLowerInvariant(), null))
                .ToList();

            var name = _pendingName ?? string.Empty;
            _pendingName = null;
            RoomEntered?.Invoke(this, new RoomObservation(null, name, observed));
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological line against a user's pattern: skipped, never the session's problem.
        }
    }
}
