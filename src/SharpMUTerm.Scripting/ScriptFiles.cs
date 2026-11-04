using System.Text;

namespace SharpMUTerm.Scripting;

/// <summary>
/// The script files a player can open, create and save from inside the client (⌃P ▸ <em>Edit script…</em>, the script
/// console's ⌃E, <c>/script new|edit</c>). Every path goes through <see cref="SessionScripts.Resolve"/>, the rule the
/// loader already applies, so the editor can reach exactly the files the loader can read and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>A name, never a path.</b> What a player types is a relative name with <c>/</c> between folders. It is refused
/// when it is absolute, holds <c>..</c> or a backslash, is not <c>.lua</c>, or resolves outside the root. Where an
/// existing file or folder on the way is a link, the save is refused too: a link inside the root that points out of it
/// would otherwise turn a save into a write anywhere.
/// </para>
/// <para>
/// <b>A save is atomic</b>: the text is written to a temporary file beside the target and renamed over it, so a crash
/// or a full disk leaves the old file whole rather than half a script the next reload would run.
/// </para>
/// </remarks>
public static class ScriptFiles
{
    /// <summary>The extension every script has.</summary>
    public const string Extension = ".lua";

    /// <summary>The most files listed.</summary>
    public const int MaxListed = 200;

    /// <summary>The longest name accepted.</summary>
    public const int MaxNameLength = 120;

    /// <summary>The largest file the editor opens or saves.</summary>
    public const int MaxBytes = 512 * 1024;

    /// <summary>The prefix a package's script carries in a trigger set.</summary>
    public const string PackagePrefix = "package:";

    /// <summary>What a new script starts as.</summary>
    public const string Template =
        "-- A script for this character. Saved under the scripts folder; ⌃S saves and reloads.\n" +
        "-- Name it in a trigger set's scripts (F5) to load it, and call its functions from a rule's script field.\n" +
        "\n" +
        "function greet(line)\n" +
        "  print('saw: ' .. line)\n" +
        "end\n";

    /// <summary>
    /// The <c>.lua</c> files under <paramref name="root"/>, as names relative to it with <c>/</c> between folders, sorted,
    /// at most <see cref="MaxListed"/>. A missing or unreadable root lists nothing.
    /// </summary>
    public static IReadOnlyList<string> List(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(full))
        {
            return [];
        }

        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 6 };
            return Directory.EnumerateFiles(full, "*" + Extension, options)
                .Select(path => Path.GetRelativePath(full, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(name => Check(name) is null)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Take(MaxListed)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// What is wrong with <paramref name="name"/> as a script's name, or null when it is one: relative, <c>/</c>
    /// between folders, no <c>..</c>, no backslash or control character, ending in <c>.lua</c>.
    /// </summary>
    public static string? Check(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "give the script a name, such as combat.lua";
        }

        if (name.Length > MaxNameLength)
        {
            return $"a script's name is at most {MaxNameLength} characters";
        }

        if (name.Any(char.IsControl) || name.Contains('\\', StringComparison.Ordinal) || name.Contains(':', StringComparison.Ordinal))
        {
            return "a script's name has no backslash, colon or control character — use / between folders";
        }

        if (name.StartsWith('/') || Path.IsPathRooted(name))
        {
            return "a script's name is relative to the scripts folder, not a path from the root of the disk";
        }

        var parts = name.Split('/');
        if (parts.Any(p => p.Length == 0 || p == "." || p == ".."))
        {
            return "a script's name cannot leave the scripts folder (no .. and no empty folder names)";
        }

        if (!name.EndsWith(Extension, StringComparison.Ordinal) || name.Length == Extension.Length)
        {
            return "a script's name ends in .lua";
        }

        return null;
    }

    /// <summary>
    /// A typed name made into a script's: <c>.lua</c> added when it has no extension, surrounding space trimmed. Still
    /// to be <see cref="Check"/>ed.
    /// </summary>
    public static string Normalise(string? typed)
    {
        var name = (typed ?? string.Empty).Trim();
        return name.Length > 0 && !Path.HasExtension(name) ? name + Extension : name;
    }

    /// <summary>
    /// The full path of script <paramref name="name"/> under <paramref name="root"/>, or why it has none: a name that
    /// fails <see cref="Check"/>, or one that resolves outside the root.
    /// </summary>
    public static (string? Path, string? Error) PathOf(string root, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (Check(name) is { } why)
        {
            return (null, why);
        }

        return SessionScripts.Resolve(root, name) is { } path
            ? (path, null)
            : (null, "that name is outside the scripts folder");
    }

    /// <summary>The text of script <paramref name="name"/>, or why it cannot be read.</summary>
    public static (string? Text, string? Error) Read(string root, string name)
    {
        var (path, error) = PathOf(root, name);
        if (path is null)
        {
            return (null, error);
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return (null, $"{name} does not exist yet");
            }

            if (info.Length > MaxBytes)
            {
                return (null, $"{name} is larger than {MaxBytes / 1024} KB — edit it outside the client");
            }

            return (File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"{name} could not be read: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes <paramref name="text"/> to script <paramref name="name"/> under <paramref name="root"/>, line for line as
    /// typed (line breaks as <c>\n</c>, one at the end): to a temporary file beside it, then renamed over it. Creates the
    /// script's folders inside the root. Returns null when it was written, otherwise why not; nothing is changed on a
    /// refusal.
    /// </summary>
    /// <param name="mustBeNew">True for a new script: refused when the file already exists.</param>
    public static string? Save(string root, string name, string text, bool mustBeNew = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (path, error) = PathOf(root, name);
        if (path is null)
        {
            return error;
        }

        var body = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (body.Length > 0 && !body.EndsWith('\n'))
        {
            body += "\n";
        }

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(body);
        if (bytes.Length > MaxBytes)
        {
            return $"a script is at most {MaxBytes / 1024} KB";
        }

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(fullRoot))
        {
            return $"there is no scripts folder — create {fullRoot} first";
        }

        if (LinkOnTheWay(fullRoot, path) is { } link)
        {
            return $"{link} is a link, and a save never follows one out of the scripts folder";
        }

        if (mustBeNew && File.Exists(path))
        {
            return $"{name} already exists — open it with Edit script";
        }

        var folder = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(folder);
            if (LinkOnTheWay(fullRoot, path) is { } created)
            {
                return $"{created} is a link, and a save never follows one out of the scripts folder";
            }

            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Nothing more to do: the target was never touched.
            }

            return $"{name} was not saved: {ex.Message}";
        }
    }

    /// <summary>The first file or folder from the root down to <paramref name="path"/> that exists and is a link, by its name relative to the root.</summary>
    private static string? LinkOnTheWay(string fullRoot, string path)
    {
        var relative = Path.GetRelativePath(fullRoot, path);
        var current = fullRoot;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.Exists && info.LinkTarget is not null)
            {
                return Path.GetRelativePath(fullRoot, current).Replace(Path.DirectorySeparatorChar, '/');
            }
        }

        return null;
    }
}
