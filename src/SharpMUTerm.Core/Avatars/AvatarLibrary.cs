namespace SharpMUTerm.Core.Avatars;

/// <summary>
/// The client's own copies of the pictures characters and worlds use: <c>avatars/</c> beside <c>config.json</c>, one
/// cropped PNG a picture, named by a fresh id. A picture chosen from a file or the web is copied here once and the setting
/// is pointed at the copy, so deleting or moving the original changes nothing and a web picture is downloaded once.
/// <para>
/// A library with no folder keeps nothing (the <c>save:</c> family): a test and a snapshot copy no picture.
/// </para>
/// </summary>
public sealed class AvatarLibrary
{
    /// <summary>The side of a kept copy, in pixels: twice the 64 the client draws, so a re-crop or a larger disc keeps detail.</summary>
    public const int CopySize = 128;

    private const string Prefix = "avatar-";

    /// <param name="root">The folder copies are kept in, or null to keep none.</param>
    public AvatarLibrary(string? root = null)
    {
        Root = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);
    }

    /// <summary>The folder, or null when copies are not kept.</summary>
    public string? Root { get; }

    /// <summary>The folder beside <paramref name="configPath"/>.</summary>
    public static string PathFor(string configPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "avatars");

    /// <summary>Whether <paramref name="source"/> is already one of this library's copies.</summary>
    public bool IsCopy(string? source)
    {
        if (Root is null || string.IsNullOrWhiteSpace(source) || AvatarSource.IsWeb(source.Trim()))
        {
            return false;
        }

        var path = AvatarSource.ExpandPath(source);
        return path is not null
            && string.Equals(Path.GetDirectoryName(path), Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && Path.GetFileName(path).StartsWith(Prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes <paramref name="png"/> as a new copy and returns its path, or null when no folder is kept or the write failed.
    /// Written to a temporary name and renamed, so a half-written copy is never named by a setting.
    /// </summary>
    public string? Save(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (Root is null)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(Root);
            var path = Path.Combine(Root, Prefix + Guid.NewGuid().ToString("N") + ".png");
            var temporary = path + ".new";
            File.WriteAllBytes(temporary, png);
            File.Move(temporary, path);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes every copy no setting names (<paramref name="inUse"/>), and any half-written one. Run at start only: an undo
    /// can put back a setting that named a copy, and a copy deleted during the run would leave it showing the letter.
    /// </summary>
    public void Prune(IEnumerable<string?> inUse)
    {
        ArgumentNullException.ThrowIfNull(inUse);
        if (Root is null || !Directory.Exists(Root))
        {
            return;
        }

        var keep = new HashSet<string>(
            inUse.Where(IsCopy).Select(s => AvatarSource.ExpandPath(s!)!),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, Prefix + "*"))
            {
                if (!keep.Contains(Path.GetFullPath(file)))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A copy that cannot be deleted now is deleted at a later start.
        }
    }
}
