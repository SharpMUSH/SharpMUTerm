using System.Text.Json;

namespace SharpMUTerm.Core.Commands;

/// <summary>
/// The words ⇥ can complete on the command line: the people the game has named over GMCP first, then the
/// words of recent output, newest first. One per connection, fed on the read loop and asked on the UI
/// thread, so every member takes the same lock.
/// </summary>
public sealed class CompletionIndex
{
    /// <summary>How many distinct words of output are kept; the oldest goes first.</summary>
    public const int MaxWords = 4096;

    /// <summary>How many names are kept.</summary>
    public const int MaxNames = 512;

    /// <summary>A word shorter than this is quicker typed than completed.</summary>
    public const int MinWordLength = 3;

    /// <summary>A longer run is not a word anybody types.</summary>
    public const int MaxWordLength = 32;

    private readonly Lock _gate = new();
    private readonly Recency _words = new(MaxWords);
    private readonly Recency _names = new(MaxNames);

    /// <summary>Takes the words of one line of output.</summary>
    public void ObserveLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            foreach (var word in Words(text))
            {
                _words.Touch(word);
            }
        }
    }

    /// <summary>Takes a name the game said is a person: in the room, on a channel, talking.</summary>
    public void ObserveName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxWordLength || name.Any(char.IsWhiteSpace))
        {
            return;
        }

        lock (_gate)
        {
            _names.Touch(name);
        }
    }

    /// <summary>
    /// Reads the names out of the GMCP messages that carry them: <c>Room.Players</c>, <c>Room.AddPlayer</c>,
    /// <c>Comm.Channel.Players</c> and <c>Comm.Channel.Text</c>'s talker. Anything else, or anything malformed,
    /// is ignored.
    /// </summary>
    public void ObserveGmcp(string package, string json)
    {
        if (package is not ("Room.Players" or "Room.AddPlayer" or "Comm.Channel.Players" or "Comm.Channel.Text"))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            switch (package)
            {
                case "Comm.Channel.Text":
                    ObserveName(Property(root, "talker"));
                    break;
                case "Room.AddPlayer":
                    ObserveName(Property(root, "name"));
                    break;
                default:
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray().Take(MaxNames))
                        {
                            ObserveName(item.ValueKind == JsonValueKind.String ? item.GetString() : Property(item, "name"));
                        }
                    }

                    break;
            }
        }
        catch (JsonException)
        {
        }
    }

    /// <summary>
    /// What <paramref name="prefix"/> completes to, best first: names, then words, each newest first, matched
    /// without regard to case and never the prefix itself.
    /// </summary>
    public IReadOnlyList<string> Candidates(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length == 0)
        {
            return [];
        }

        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { prefix };
            return _names.Newest().Concat(_words.Newest())
                .Where(w => w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && seen.Add(w))
                .ToList();
        }
    }

    /// <summary>Forgets everything, as a new connection does.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _words.Clear();
            _names.Clear();
        }
    }

    /// <summary>The runs of letters, digits, <c>_</c>, <c>'</c> and <c>-</c> in a line, trimmed of trailing punctuation.</summary>
    internal static IEnumerable<string> Words(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '\'' or '-');
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                var word = text[start..i].TrimEnd('\'', '-').TrimStart('\'', '-');
                start = -1;
                if (word.Length is >= MinWordLength and <= MaxWordLength && word.Any(char.IsLetter))
                {
                    yield return word;
                }
            }
        }
    }

    private static string? Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: JsonValueKind.String } found
            ? found.Value.GetString()
            : null;

    /// <summary>A capped set kept in order of last use, holding each word in the casing it was last seen in.</summary>
    private sealed class Recency(int capacity)
    {
        private readonly LinkedList<string> _order = new();
        private readonly Dictionary<string, LinkedListNode<string>> _nodes = new(StringComparer.OrdinalIgnoreCase);

        public void Touch(string word)
        {
            if (_nodes.Remove(word, out var node))
            {
                _order.Remove(node);
            }

            _nodes[word] = _order.AddFirst(word);
            if (_nodes.Count > capacity && _order.Last is { } last)
            {
                _nodes.Remove(last.Value);
                _order.RemoveLast();
            }
        }

        public IEnumerable<string> Newest() => _order;

        public void Clear()
        {
            _order.Clear();
            _nodes.Clear();
        }
    }
}
