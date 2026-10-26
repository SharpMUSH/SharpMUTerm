using TelnetNegotiationCore.Gmcp;

namespace SharpMUTerm.Core.Media;

/// <summary>Whether a request is a sound effect or music. MCMP's third kind, video, is not played.</summary>
public enum MediaKind
{
    Sound,
    Music,
}

/// <summary>
/// One thing to play, as MCMP's <c>Client.Media.Play</c> describes it, with the specification's defaults filled
/// in. MSP's <c>!!SOUND</c> and <c>!!MUSIC</c> become one of these too, so one queue applies one set of rules.
/// </summary>
public sealed record MediaRequest(string Name)
{
    /// <summary>The folder the file is fetched from, when the server's default does not apply.</summary>
    public string? Url { get; init; }

    public MediaKind Kind { get; init; } = MediaKind.Sound;

    public string? Tag { get; init; }

    /// <summary>1 to 100, relative to the player's own volume; the specification's default is 50.</summary>
    public int Volume { get; init; } = 50;

    public TimeSpan FadeIn { get; init; }

    public TimeSpan FadeOut { get; init; }

    public TimeSpan Start { get; init; }

    public TimeSpan? Finish { get; init; }

    /// <summary>How many times it plays; −1 loops until stopped.</summary>
    public int Loops { get; init; } = 1;

    /// <summary>1 to 100, or null for none. While it plays, media of lower priority is halted and refused.</summary>
    public int? Priority { get; init; }

    /// <summary>Music only: true leaves the same file playing when it is asked for again, false restarts it.</summary>
    public bool Continue { get; init; } = true;

    /// <summary>A slot: a different file played under the same key replaces the one before.</summary>
    public string? Key { get; init; }

    public string? Caption { get; init; }

    /// <summary>
    /// MSP's rule for sounds: one at a time, and a new one replaces the one playing only at a higher priority.
    /// MCMP plays sounds alongside each other.
    /// </summary>
    public bool Exclusive { get; init; }

    /// <summary>The words a caption line shows: the server's caption, else the file's name without its folder or extension.</summary>
    public string CaptionText =>
        !string.IsNullOrWhiteSpace(Caption) ? Caption.Trim() : Path.GetFileNameWithoutExtension(Name.Replace('\\', '/').Split('/')[^1]);
}

/// <summary>
/// Which playing media a stop or a pause applies to: every field that is set has to match, and a filter with
/// nothing set matches everything ("an empty body will stop all media").
/// </summary>
public sealed record MediaFilter
{
    public string? Name { get; init; }

    public MediaKind? Kind { get; init; }

    public string? Tag { get; init; }

    /// <summary>Matches media of this priority or lower.</summary>
    public int? Priority { get; init; }

    public string? Key { get; init; }

    public static MediaFilter All { get; } = new();

    public bool Matches(MediaRequest media) =>
        (Name is null || string.Equals(Name, media.Name, StringComparison.Ordinal))
        && (Kind is null || Kind == media.Kind)
        && (Tag is null || string.Equals(Tag, media.Tag, StringComparison.OrdinalIgnoreCase))
        && (Priority is null || (media.Priority ?? 0) <= Priority)
        && (Key is null || string.Equals(Key, media.Key, StringComparison.Ordinal));
}

/// <summary>What a server asked the media queue to do.</summary>
public abstract record MediaCommand
{
    /// <summary>
    /// The command a <c>Client.Media.*</c> GMCP message carries, or null for anything else — another package,
    /// a body that does not parse, or a video, which this client does not play.
    /// </summary>
    public static MediaCommand? FromGmcp(string package, string json)
    {
        switch (package)
        {
            case GmcpPackages.ClientMediaDefault or GmcpPackages.ClientMediaObsoleteDefault:
                return MediaDefault.TryParse(json, out var d) ? new SetDefault(d.Url) : null;
            case GmcpPackages.ClientMediaLoad:
                return TelnetNegotiationCore.Gmcp.MediaLoad.TryParse(json, out var l) ? new Load(l.Name, l.Url) : null;
            case GmcpPackages.ClientMediaPlay:
                if (!MediaPlay.TryParse(json, out var p) || p.Type == MediaType.Video)
                {
                    return null;
                }

                return new Play(new MediaRequest(p.Name)
                {
                    Url = p.Url,
                    Kind = p.Type == MediaType.Music ? MediaKind.Music : MediaKind.Sound,
                    Tag = p.Tag,
                    Volume = Math.Clamp(p.Volume ?? 50, 1, 100),
                    FadeIn = Milliseconds(p.FadeIn),
                    FadeOut = Milliseconds(p.FadeOut),
                    Start = Milliseconds(p.Start),
                    Finish = p.Finish is > 0 ? Milliseconds(p.Finish) : null,
                    Loops = p.Loops is -1 or > 0 ? p.Loops.Value : 1,
                    Priority = p.Priority is { } priority ? Math.Clamp(priority, 1, 100) : null,
                    Continue = p.Continue ?? true,
                    Key = p.Key,
                    Caption = p.Caption,
                });
            case GmcpPackages.ClientMediaStop:
                if (!MediaStop.TryParse(json, out var s) || s.Type == MediaType.Video)
                {
                    return null;
                }

                return new Stop(ToFilter(s.Name, s.Type, s.Tag, s.Priority, s.Key), s.FadeAway == true, s.FadeOut is > 0 ? Milliseconds(s.FadeOut) : null);
            case GmcpPackages.ClientMediaPause:
                return MediaPause.TryParse(json, out var pause) && pause.Type != MediaType.Video
                    ? new Pause(ToFilter(pause.Name, pause.Type, pause.Tag, pause.Priority, pause.Key))
                    : null;
            default:
                return null;
        }
    }

    private static MediaFilter ToFilter(string? name, MediaType? type, string? tag, int? priority, string? key) => new()
    {
        Name = name,
        Kind = type switch { MediaType.Music => MediaKind.Music, MediaType.Sound => MediaKind.Sound, _ => null },
        Tag = tag,
        Priority = priority,
        Key = key,
    };

    private static TimeSpan Milliseconds(int? value) => value is > 0 ? TimeSpan.FromMilliseconds(value.Value) : TimeSpan.Zero;

    /// <summary>Play something.</summary>
    public sealed record Play(MediaRequest Request) : MediaCommand;

    /// <summary>Stop what matches; <paramref name="FadeAway"/> fades it out first.</summary>
    public sealed record Stop(MediaFilter Filter, bool FadeAway = false, TimeSpan? FadeOut = null) : MediaCommand;

    /// <summary>Pause what matches; a later matching play resumes it.</summary>
    public sealed record Pause(MediaFilter Filter) : MediaCommand;

    /// <summary>Fetch a file now, so it is ready when played.</summary>
    public sealed record Load(string Name, string? Url) : MediaCommand;

    /// <summary>The folder files are fetched from when a request names none.</summary>
    public sealed record SetDefault(string Url) : MediaCommand;
}
