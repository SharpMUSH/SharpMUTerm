using System.Globalization;
using System.Text.RegularExpressions;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Core.Mapping;

/// <summary>
/// Rooms from MXP: the text of the elements a server flags <c>RoomNum</c>, <c>RoomName</c> and <c>RoomExit</c>
/// (or the spec's line tags 10–12, which flag the same). A name, then its exits, complete a room; a number seen
/// before them is its id. <c>RoomDesc</c> is not needed to place a room and is not read.
/// </summary>
public sealed class MxpRoomSource : IRoomSource
{
    private static readonly Regex ExitSeparator = new(@"\s*(?:,|\band\b|\s)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private readonly WorldSession _session;
    private string? _name;
    private long? _id;

    public MxpRoomSource(WorldSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.MxpFlagged += OnFlagged;
    }

    public event EventHandler<RoomObservation>? RoomEntered;

    public void Dispose() => _session.MxpFlagged -= OnFlagged;

    private void OnFlagged(object? sender, MxpFlagText e) => Observe(e.Flag, e.Text);

    /// <summary>One flagged element's text. Public so the reading can be driven without a session.</summary>
    public void Observe(string flag, string text)
    {
        switch (flag.Trim().ToLowerInvariant())
        {
            case "roomnum":
                _id = long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
                break;
            case "roomname":
                _name = text.Trim();
                break;
            case "roomexit":
                if (_name is null)
                {
                    return;
                }

                var list = text.Trim();
                var colon = list.IndexOf(':', StringComparison.Ordinal);
                if (colon >= 0 && colon < 12)
                {
                    list = list[(colon + 1)..];
                }

                var exits = ExitSeparator.Split(list)
                    .Select(x => x.Trim().Trim('.', '[', ']', '(', ')'))
                    .Where(x => x.Length > 0 && !x.Equals("none", StringComparison.OrdinalIgnoreCase))
                    .Select(x => new ObservedExit(MapDirections.Normalise(x) ?? x.ToLowerInvariant(), null))
                    .ToList();
                RoomEntered?.Invoke(this, new RoomObservation(_id, _name, exits));
                _name = null;
                _id = null;
                break;
        }
    }
}
