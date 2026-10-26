namespace SharpMUTerm.Core.Packages;

/// <summary>
/// The numbers the review screen shows before anything is installed. <see cref="Sending"/> is the one that
/// matters most: a rule that sends a command does so as the player.
/// </summary>
public sealed record PackageReview(
    int Sets,
    int Triggers,
    int Aliases,
    int Macros,
    int Timers,
    int Sending,
    IReadOnlyList<(string Name, long Bytes)> Scripts,
    int MediaFiles,
    long MediaBytes,
    IReadOnlyList<string> Ignored)
{
    public static PackageReview Of(PackageContents contents)
    {
        var sets = contents.Sets;
        var sending =
            sets.Sum(s => s.Triggers.Count(t => !string.IsNullOrWhiteSpace(t.Actions.SendResponse) || !string.IsNullOrWhiteSpace(t.Actions.ScriptCallback)))
            + sets.Sum(s => s.Aliases.Count)
            + sets.Sum(s => s.Macros.Count(m => !string.IsNullOrWhiteSpace(m.Command)))
            + sets.Sum(s => s.Timers.Count(t => !string.IsNullOrWhiteSpace(t.Command)));
        return new PackageReview(
            sets.Count,
            sets.Sum(s => s.Triggers.Count),
            sets.Sum(s => s.Aliases.Count),
            sets.Sum(s => s.Macros.Count),
            sets.Sum(s => s.Timers.Count),
            sending,
            contents.Scripts.Select(kv => (kv.Key, (long)kv.Value.Length)).ToList(),
            contents.Media.Count,
            contents.Media.Values.Sum(v => (long)v.Length),
            contents.Ignored);
    }
}
