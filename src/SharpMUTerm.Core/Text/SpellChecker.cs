using WeCantSpell.Hunspell;

namespace SharpMUTerm.Core.Text;

/// <summary>A word the checker does not know, as a range of one line.</summary>
public readonly record struct Misspelling(int Start, int Length);

/// <summary>
/// Spelling for the F1 composer: a Hunspell dictionary (the <c>.aff</c>/<c>.dic</c> pair LibreOffice and every
/// Linux distribution ship), plus the words a player has added.
/// </summary>
/// <remarks>
/// <para>
/// <b>No dictionary is bundled.</b> One is found in the folders <see cref="Find"/> is given — the client's own
/// <c>dictionaries/</c> folder first, then the system's — and when there is none the composer says so rather
/// than marking nothing and looking as if every word were right.
/// </para>
/// <para>
/// <b>What is not a word to check.</b> A MUSH post is half markup, so a run that follows a <c>%</c> loses its
/// first letter (<c>%rHello</c> is a break and <c>Hello</c>), a run directly before <c>(</c> is a function
/// name, and a run in capitals, or holding a digit or an inner capital, is an acronym, a dbref or a name.
/// One letter is never marked.
/// </para>
/// <para>
/// <b>Added words are a plain list</b>, one per line, kept beside the dictionaries. A null path keeps them for
/// the run only, which is what a test and a snapshot get.
/// </para>
/// </remarks>
public sealed class SpellChecker
{
    private readonly WordList _words;
    private readonly HashSet<string> _added = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _cache = new(StringComparer.Ordinal);
    private readonly string? _userWordsPath;

    /// <summary>Most cached answers kept; past this the cache is dropped and refilled.</summary>
    private const int CacheLimit = 20_000;

    private SpellChecker(WordList words, string name, string? userWordsPath)
    {
        _words = words;
        Name = name;
        _userWordsPath = userWordsPath;
        if (userWordsPath is not null && File.Exists(userWordsPath))
        {
            foreach (var line in File.ReadLines(userWordsPath))
            {
                var word = line.Trim();
                if (word.Length > 0)
                {
                    _added.Add(word);
                }
            }
        }
    }

    /// <summary>The dictionary's name, from its file (<c>en_US</c>).</summary>
    public string Name { get; }

    /// <summary>The words added by hand, for the tests.</summary>
    public IReadOnlyCollection<string> AddedWords => _added;

    /// <summary>A checker over a dictionary already open, for the tests.</summary>
    public static SpellChecker FromStreams(Stream dic, Stream aff, string name = "test", string? userWordsPath = null) =>
        new(WordList.CreateFromStreams(dic, aff), name, userWordsPath);

    /// <summary>
    /// Opens the dictionary to use from <paramref name="folders"/>, in order, or returns null when none of them
    /// holds one. Within a folder the one named for <paramref name="language"/> wins (<c>en_GB</c>), then one in
    /// its language (<c>en_*</c>), then the first by name.
    /// </summary>
    /// <param name="language">A locale as <c>en_GB</c> or <c>en-GB</c>, or null for no preference.</param>
    public static SpellChecker? Find(IEnumerable<string> folders, string? language, string? userWordsPath)
    {
        var wanted = (language ?? string.Empty).Replace('-', '_');
        var prefix = wanted.Split('_')[0] + "_";

        foreach (var folder in folders)
        {
            string[] dics;
            try
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                dics = Directory.GetFiles(folder, "*.dic")
                    .Where(d => File.Exists(Path.ChangeExtension(d, ".aff")))
                    .Order(StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (dics.Length == 0)
            {
                continue;
            }

            var chosen = dics.FirstOrDefault(d => wanted.Length > 0 && string.Equals(Path.GetFileNameWithoutExtension(d), wanted, StringComparison.OrdinalIgnoreCase))
                ?? dics.FirstOrDefault(d => prefix.Length > 1 && Path.GetFileNameWithoutExtension(d).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                ?? dics[0];

            try
            {
                return new SpellChecker(
                    WordList.CreateFromFiles(chosen, Path.ChangeExtension(chosen, ".aff")),
                    Path.GetFileNameWithoutExtension(chosen),
                    userWordsPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
            {
                continue;
            }
        }

        return null;
    }

    /// <summary>
    /// The folders a platform keeps Hunspell dictionaries in, after <paramref name="own"/>. Only folders, not a
    /// promise any exists: <see cref="Find"/> skips what is not there.
    /// </summary>
    public static IReadOnlyList<string> SearchFolders(string? own)
    {
        var folders = new List<string>();
        if (own is not null)
        {
            folders.Add(own);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            folders.Add(Path.Combine(home, "Library", "Spelling"));
            folders.Add("/Library/Spelling");
        }
        else if (!OperatingSystem.IsWindows())
        {
            folders.Add(Path.Combine(home, ".local", "share", "hunspell"));
            folders.Add("/usr/share/hunspell");
            folders.Add("/usr/local/share/hunspell");
            folders.Add("/usr/share/myspell");
            folders.Add("/usr/share/myspell/dicts");
        }

        return folders;
    }

    /// <summary>
    /// The user's language from the POSIX locale variables (<c>LC_ALL</c>, <c>LC_MESSAGES</c>, <c>LANG</c>), as
    /// <c>en_GB</c>, or null. Read from the environment because the client runs with invariant globalization,
    /// where every <c>CultureInfo</c> is the invariant one and names no language.
    /// </summary>
    public static string? PreferredLanguage()
    {
        foreach (var name in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value) || value is "C" or "POSIX" || value.StartsWith("C.", StringComparison.Ordinal))
            {
                continue;
            }

            return value.Split('.', '@')[0];
        }

        return null;
    }

    /// <summary>Whether a word is spelt right, or was added.</summary>
    public bool Check(string word)
    {
        if (_added.Contains(word))
        {
            return true;
        }

        if (_cache.TryGetValue(word, out var known))
        {
            return known;
        }

        if (_cache.Count >= CacheLimit)
        {
            _cache.Clear();
        }

        var ok = _words.Check(word);
        _cache[word] = ok;
        return ok;
    }

    /// <summary>The words on <paramref name="line"/> this checker does not know, in order.</summary>
    public IReadOnlyList<Misspelling> Misspellings(string line)
    {
        var found = new List<Misspelling>();
        foreach (var (start, length) in Words(line))
        {
            if (!Check(line.Substring(start, length)))
            {
                found.Add(new Misspelling(start, length));
            }
        }

        return found;
    }

    /// <summary>
    /// Adds a word, so it is never marked again, and appends it to the list on disk. Returns false for
    /// something that is not one word.
    /// </summary>
    public bool Add(string word)
    {
        word = word.Trim();
        if (word.Length == 0 || word.Any(c => !IsWordChar(c)))
        {
            return false;
        }

        if (!_added.Add(word))
        {
            return true;
        }

        if (_userWordsPath is { } path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, word + "\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kept for the run; the next launch will mark it again, which is the whole cost.
            }
        }

        return true;
    }

    /// <summary>The word at <paramref name="column"/> on <paramref name="line"/> (or just before it), or null.</summary>
    public static string? WordAt(string line, int column)
    {
        foreach (var (start, length) in Runs(line))
        {
            if (column >= start && column <= start + length)
            {
                return line.Substring(start, length);
            }
        }

        return null;
    }

    /// <summary>The runs of <paramref name="line"/> that are words to check, by the rules in the remarks.</summary>
    internal static IEnumerable<(int Start, int Length)> Words(string line)
    {
        foreach (var (runStart, runLength) in Runs(line))
        {
            var start = runStart;
            var length = runLength;
            if (start > 0 && line[start - 1] == '%')
            {
                start++;
                length--;
            }

            var end = start + length;
            if (length < 2 || (end < line.Length && line[end] == '('))
            {
                continue;
            }

            var word = line.AsSpan(start, length);
            if (word.ContainsAny("0123456789") || HasInnerCapital(word))
            {
                continue;
            }

            yield return (start, length);
        }
    }

    /// <summary>Letters, digits and inner apostrophes, the way a word processor splits a line.</summary>
    private static IEnumerable<(int Start, int Length)> Runs(string line)
    {
        var i = 0;
        while (i < line.Length)
        {
            if (!char.IsLetterOrDigit(line[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < line.Length
                   && (char.IsLetterOrDigit(line[i])
                       || (line[i] is '\'' or '’' && i + 1 < line.Length && char.IsLetter(line[i + 1]) && i > start)))
            {
                i++;
            }

            yield return (start, i - start);
        }
    }

    private static bool HasInnerCapital(ReadOnlySpan<char> word)
    {
        for (var i = 1; i < word.Length; i++)
        {
            if (char.IsUpper(word[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '\'' or '’';
}
