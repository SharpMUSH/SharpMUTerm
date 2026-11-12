using MarkupString;
using MarkupString.Ansi;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using MarkupString.Mxp.Decoding;
using MarkupString.Pueblo.Decoding;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Media;
using SharpMUTerm.Core.Protocols;
using MsDecoder = MarkupString.Decoding.Decoder;

namespace SharpMUTerm.Core.Text;

/// <summary>
/// Reads what a server sends into <see cref="StyledLine"/>s, through MarkupString's <see cref="MsDecoder"/>: ANSI and
/// VT sequences always, and MXP or Pueblo when the connection turns one on. One per connection; it holds that
/// connection's style, open elements, definitions and pending line.
/// <para>
/// <b><see cref="Flush"/> is the line boundary.</b> The telnet layer strips each line's terminator, so a session
/// feeds a line's text and then flushes; the flush gives the decoder the line feed back, so everything the end of a
/// line does (MXP's line mode reverting, open-mode tags closing) happens there, and Pueblo's HTML mode, where a line
/// feed is whitespace, still waits for its <c>&lt;br&gt;</c>.
/// </para>
/// <para>
/// The decoded <see cref="MarkupText"/> is folded into SharpMUTerm's own line model here: SGR, MXP and Pueblo styles
/// into <see cref="TextStyle"/> (palette colours stay palette colours, so a theme can still remap them), links,
/// commands and menus into <see cref="SpanInteraction"/>, an MXP picture into an <see cref="InlineImageRequest"/>, and
/// the protocol's other output (entities, room flags, gauges, sounds, frames) into this class's events.
/// </para>
/// </summary>
public sealed class MarkupLineParser
{
    /// <summary>The most windows one connection's frames may name; past it, text stays in the main window.</summary>
    public const int MaxFrames = 8;

    /// <summary>The most commands one MXP <c>&lt;SEND&gt;</c> menu keeps.</summary>
    public const int MaxSendChoices = 20;

    private const int MaxFrameNameLength = 32;
    private const int MaxCaptionLength = 64;

    /// <summary>Every protocol this client reads, shared by every connection.</summary>
    /// <para>
    /// A tab is kept as a tab, for the session to expand at the player's tab width
    /// (<see cref="StyledText.ExpandTabs"/>), rather than moved to the next multiple of 8 here. MXP's refused tags are
    /// shown as the text they were, and its <c>&lt;SUPPORT&gt;</c> answer is <see cref="MxpClientSupport"/>'s.
    /// </para>
    public static DecoderRegistry Registry { get; } = DecoderRegistry.Empty
        .WithVt()
        .With(new LiteralTabs())
        .WithMxp(new MxpOptions { ShowRefusedTags = true, Supports = MxpClientSupport.Supports })
        .WithPueblo();

    private readonly MsDecoder _decoder;
    private readonly HashSet<string> _frames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _entities = new(StringComparer.OrdinalIgnoreCase);

    // The flagged element being read, which may span lines: its markup instance and the text so far.
    private MxpFlagMarkup? _flag;
    private readonly System.Text.StringBuilder _flagText = new();

    // Whether the text of the line being read had any markup in it, so a line it all went into is not a blank one.
    private bool _markupThisLine;
    private bool _textThisLine;

    /// <summary>A reader for <paramref name="format"/>: ANSI alone, or with MXP or Pueblo on from the start.</summary>
    public MarkupLineParser(ContentFormat format = ContentFormat.Ansi)
    {
        _decoder = Registry.CreateDecoder(new DecoderOptions
        {
            Identity = Identity,
        });
        _decoder.EventRaised += OnEvent;
        Format = ContentFormat.Ansi;
        Switch(format);
    }

    /// <summary>What this client tells a server it is, in an MXP <c>&lt;VERSION&gt;</c> answer.</summary>
    public static ClientIdentity Identity { get; } = new()
    {
        Name = MxpClientSupport.ClientName,
        Version = MxpClientSupport.ClientVersion,
        // Only MXP's VERSION and SUPPORT are answered. The plain ANSI reader this replaced answered nothing, and a
        // scrolling pane is not the screen a device-attributes or cursor query assumes.
        Answers = ClientAnswers.Mxp,
    };

    /// <summary>The markup this reader reads on top of ANSI.</summary>
    public ContentFormat Format { get; private set; }

    /// <summary>A line the server is owed: the answer to an MXP <c>&lt;VERSION&gt;</c> or <c>&lt;SUPPORT&gt;</c>, without its line terminator.</summary>
    public event EventHandler<string>? ClientReply;

    /// <summary>A server's entity was set or deleted: <c>&lt;!ENTITY&gt;</c>, <c>&lt;VAR&gt;</c>, or an element's <c>FLAG="set x"</c>.</summary>
    public event EventHandler<MxpEntityChange>? EntityChanged;

    /// <summary>The text of an element whose <c>FLAG</c> says what it is (<c>RoomName</c>, <c>RoomDesc</c>, <c>RoomExit</c>…).</summary>
    public event EventHandler<MxpFlagText>? Flagged;

    /// <summary>A <c>&lt;GAUGE&gt;</c> or <c>&lt;STAT&gt;</c> on a secure line.</summary>
    public event EventHandler<MxpGauge>? GaugeDeclared;

    /// <summary>A sound or music request (<c>&lt;SOUND&gt;</c>, <c>&lt;MUSIC&gt;</c>, Pueblo's <c>xch_sound</c>).</summary>
    public event EventHandler<MediaCommand>? MediaRequested;

    /// <summary>Text a <c>&lt;DEST&gt;</c>, a redirecting <c>&lt;FRAME&gt;</c> or a Pueblo pane sent to a named window.</summary>
    public event EventHandler<MxpRedirect>? Redirected;

    /// <summary>
    /// Whether the line the last <see cref="Flush"/> ended was taken entirely by markup, or sent to another window, so
    /// that its returning null is not a blank line the server sent.
    /// </summary>
    public bool LineSwallowed { get; private set; }

    /// <summary>The value of a server entity this connection was told about, or null.</summary>
    public string? Entity(string name) => _entities.GetValueOrDefault(name);

    /// <summary>True when a partial line is buffered.</summary>
    public bool HasPendingContent => _decoder.HasPendingLine;

    /// <summary>Turns MXP or Pueblo on (or both off, for <see cref="ContentFormat.Ansi"/>), keeping the style and the pending line.</summary>
    public void Switch(ContentFormat format)
    {
        if (format == Format)
        {
            return;
        }

        if (Format != ContentFormat.Ansi)
        {
            _decoder.DisableProtocol(Format == ContentFormat.Mxp ? MxpDecoding.Protocol : PuebloDecoding.Protocol);
        }

        if (format != ContentFormat.Ansi)
        {
            _decoder.EnableProtocol(format == ContentFormat.Mxp ? MxpDecoding.Protocol : PuebloDecoding.Protocol);
        }

        if (format == ContentFormat.Pueblo)
        {
            // Read Pueblo's tags from the start, as a world set to Pueblo expects; the server's own
            // </xch_mudtext> after the handshake changes nothing then. Lines still end where telnet ends them.
            _decoder.Feed("</xch_mudtext>");
        }

        Format = format;
    }

    /// <summary>Feeds a chunk of text, returning every line a line feed (or an MXP <c>&lt;BR&gt;</c>, a Pueblo <c>&lt;br&gt;</c>) completed within it.</summary>
    public IReadOnlyList<StyledLine> Feed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains('\n', StringComparison.Ordinal))
        {
            Note(text);
            return Convert(_decoder.Feed(text));
        }

        // A line feed ends a line here even in Pueblo's HTML, where the decoder reads it as a space: it is where the
        // telnet layer would have ended it.
        var lines = new List<StyledLine>();
        var segments = text.Split('\n');
        for (var i = 0; i < segments.Length; i++)
        {
            Note(segments[i]);
            lines.AddRange(Convert(_decoder.Feed(segments[i])));
            if (i < segments.Length - 1)
            {
                lines.AddRange(Convert([_decoder.EndLine()]));
                _markupThisLine = false;
                _textThisLine = false;
            }
        }

        return lines;
    }

    /// <summary>
    /// Ends the line being read and returns it, or null when it has nothing to show: nothing was sent, it was all
    /// markup, or it went to another window (<see cref="LineSwallowed"/> tells those two from a blank line).
    /// </summary>
    public StyledLine? Flush()
    {
        var markup = _markupThisLine;
        var text = _textThisLine;
        // The telnet layer took the line's terminator: end the line here whatever the protocol makes of a line feed.
        var lines = Convert([_decoder.EndLine()], out var redirected);
        _markupThisLine = false;
        _textThisLine = false;
        LineSwallowed = false;
        var line = lines.Count > 0 ? lines[^1] : null;
        if (line is { IsEmpty: false })
        {
            return line;
        }

        LineSwallowed = redirected || (markup && text);
        return null;
    }

    /// <summary>Forgets the style, open elements, definitions and the pending line, and the windows frames named.</summary>
    public void Reset()
    {
        var format = Format;
        _decoder.Reset();
        Format = ContentFormat.Ansi;
        Switch(format);
        _frames.Clear();
        _entities.Clear();
        _flag = null;
        _flagText.Clear();
        _markupThisLine = false;
        _textThisLine = false;
        LineSwallowed = false;
    }

    /// <summary>Reads a whole string of ANSI text at once, including a last line with no line break.</summary>
    public static IReadOnlyList<StyledLine> ParseAll(string text, ContentFormat format = ContentFormat.Ansi)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parser = new MarkupLineParser(format);
        var lines = new List<StyledLine>(parser.Feed(text));
        if (parser.HasPendingContent && parser.Flush() is { } last)
        {
            lines.Add(last);
        }

        return lines;
    }

    private void Note(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        _textThisLine = true;
        if (Format != ContentFormat.Ansi && text.AsSpan().IndexOfAny('<', '\x1b') >= 0)
        {
            _markupThisLine = true;
        }
    }

    private IReadOnlyList<StyledLine> Convert(IReadOnlyList<DisplayLine> lines) => Convert(lines, out _);

    private IReadOnlyList<StyledLine> Convert(IReadOnlyList<DisplayLine> lines, out bool redirected)
    {
        redirected = false;
        if (lines.Count == 0)
        {
            return Array.Empty<StyledLine>();
        }

        var result = new List<StyledLine>(lines.Count);
        foreach (var decoded in lines)
        {
            var (line, sent) = Fold(decoded);
            redirected |= sent;
            if (line is not null)
            {
                result.Add(line);
            }
        }

        return result;
    }

    /// <summary>One decoded line as the line for the main window (null when all of it went elsewhere), and whether any went elsewhere.</summary>
    private (StyledLine? Line, bool Redirected) Fold(DisplayLine decoded)
    {
        var content = decoded.Content;
        var main = new List<StyledSpan>();
        var panes = new List<(string Name, List<StyledSpan> Spans)>();
        var lineFrame = FrameName(decoded.Destination);
        MxpFlagMarkup? flagSeen = null;
        var elements = new Dictionary<List<StyledSpan>, ElementMarkup?>(ReferenceEqualityComparer.Instance);
        var at = 0;

        foreach (var run in content.Runs)
        {
            if (run.Start > at)
            {
                Add(main, content.Text.Substring(at, run.Start - at), TextStyle.Default, null, null, elements);
            }

            at = run.End;
            var markups = run.Markups;
            if (Point(markups))
            {
                continue;
            }

            var text = content.Text.Substring(run.Start, run.Length);
            var (style, interaction, pane, flag, element) = Style(markups, ref text);
            if (flag is not null)
            {
                flagSeen = flag;
                Flag(flag, text);
            }

            if (pane is not null && FrameName(pane.Name) is { } name)
            {
                var target = panes.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (target < 0)
                {
                    panes.Add((name, new List<StyledSpan>()));
                    target = panes.Count - 1;
                }

                Add(panes[target].Spans, text, style, interaction, element, elements);
                continue;
            }

            Add(main, text, style, interaction, element, elements);
        }

        if (at < content.Text.Length)
        {
            Add(main, content.Text[at..], TextStyle.Default, null, null, elements);
        }

        EndFlags(flagSeen, lineEnd: true);

        foreach (var (name, spans) in panes)
        {
            Redirected?.Invoke(this, new MxpRedirect(name, new StyledLine(spans)));
        }

        var line = new StyledLine(main, isPrompt: decoded.Kind == LineKind.Prompt);
        if (lineFrame is not null)
        {
            if (!line.IsEmpty)
            {
                Redirected?.Invoke(this, new MxpRedirect(lineFrame, line));
            }

            return (null, true);
        }

        return (line, panes.Count > 0);
    }

    /// <summary>
    /// Appends text to a span list, joining it to the last span when it looks the same and belongs to the same element:
    /// two links, pictures or exits side by side that look alike stay two spans. <paramref name="elements"/> holds the
    /// element of each list's last span.
    /// </summary>
    private static void Add(List<StyledSpan> spans, string text, TextStyle style, SpanInteraction? interaction, ElementMarkup? element, Dictionary<List<StyledSpan>, ElementMarkup?> elements)
    {
        if (text.Length == 0)
        {
            return;
        }

        var last = elements.GetValueOrDefault(spans);
        elements[spans] = element;
        if (spans.Count > 0 && Equals(last, element) && spans[^1].Style == style && Equals(spans[^1].Interaction, interaction))
        {
            spans[^1] = new StyledSpan(spans[^1].Text + text, style, interaction);
            return;
        }

        spans.Add(new StyledSpan(text, style, interaction));
    }

    /// <summary>A run that is a point (a sound, a bell, a clear): acts on it and says so, since it has no text to show.</summary>
    private bool Point(MarkupSet markups)
    {
        var point = false;
        foreach (var markup in markups)
        {
            switch (markup)
            {
                case SoundMarkup sound:
                    MediaRequested?.Invoke(this, Sound(sound));
                    point = true;
                    break;
                case SoundStopMarkup stop:
                    MediaRequested?.Invoke(this, new MediaCommand.Stop(new MediaFilter
                    {
                        Kind = stop.Channel == SoundChannel.Music ? MediaKind.Music : stop.Channel is null ? null : MediaKind.Sound,
                    }));
                    point = true;
                    break;
                case IPointMarkup:
                    point = true;
                    break;
            }
        }

        return point;
    }

    private static MediaCommand Sound(SoundMarkup sound)
    {
        var kind = sound.Channel == SoundChannel.Music ? MediaKind.Music : MediaKind.Sound;
        var fields = new Dictionary<char, string>();
        if (sound.Volume is { } volume)
        {
            fields['V'] = volume.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (sound.Repeats is { } repeats)
        {
            fields['L'] = repeats.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (sound.Priority is { } priority)
        {
            fields['P'] = priority.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (sound.Type is { } type)
        {
            fields['T'] = type;
        }

        if (kind == MediaKind.Music)
        {
            fields['C'] = sound.Continues ? "1" : "0";
        }

        // MarkupString puts MSP's U= in front of the file; the media cache wants them apart, a relative name and the
        // folder it is fetched from.
        var name = sound.Source;
        if (Uri.TryCreate(name, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            var slash = name.LastIndexOf('/');
            fields['U'] = name[..(slash + 1)];
            name = name[(slash + 1)..];
        }

        return MspTrigger.FromFields(kind, name, fields);
    }

    /// <summary>
    /// Folds one run's markup, innermost last, into SharpMUTerm's style and interaction, and finds the pane it is sent
    /// to and the flag over it. <paramref name="text"/> is replaced for a picture, whose text is its label.
    /// </summary>
    private static (TextStyle Style, SpanInteraction? Interaction, PaneMarkup? Pane, MxpFlagMarkup? Flag, ElementMarkup? Element) Style(MarkupSet markups, ref string text)
    {
        var folded = AnsiStyle.None;
        CommandMenuMarkup? menu = null;
        ImageMarkup? image = null;
        PaneMarkup? pane = null;
        MxpFlagMarkup? flag = null;
        ElementMarkup? element = null;
        for (var i = markups.Count - 1; i >= 0; i--)
        {
            var markup = markups[i];
            if (markup is IAnsiStyleSource source && source.TryGetAnsiStyle(MarkupFormat.Ansi, out var layer))
            {
                folded = folded.Combine(layer);
            }

            switch (markup)
            {
                case CommandMenuMarkup m:
                    menu = m;
                    break;
                case ImageMarkup m:
                    image = m;
                    break;
                case PaneMarkup m:
                    pane = m;
                    break;
                case MxpFlagMarkup m:
                    flag = m;
                    break;
                case ElementMarkup m:
                    // Read outermost to innermost, so the last one is the element opened last.
                    element = m;
                    break;
            }
        }

        var style = new TextStyle(Color(folded.Foreground), Color(folded.Background), Attributes(folded));
        var interaction = Interaction(folded, menu);
        if (image is not null)
        {
            interaction = Picture(image, interaction, ref text);
        }

        return (style, interaction, pane, flag, element);
    }

    private static SpanInteraction? Interaction(AnsiStyle style, CommandMenuMarkup? menu)
    {
        if (string.IsNullOrEmpty(style.LinkUrl))
        {
            return null;
        }

        if (style.LinkKind != LinkKind.Command)
        {
            return SpanInteraction.Link(style.LinkUrl, Hint(style.LinkText));
        }

        var command = SpanInteraction.Command(style.LinkUrl, Hint(style.LinkText ?? menu?.Hint), menu?.Prompt == true);
        if (menu is null || menu.Choices.Count < 2)
        {
            return command;
        }

        var choices = menu.Choices.Take(MaxSendChoices).Select(c => new SendChoice(c.Label, c.Command)).ToArray();
        return command with { Choices = choices };
    }

    private static string? Hint(string? hint) => string.IsNullOrEmpty(hint) ? null : hint;

    /// <summary>
    /// An MXP picture: its text becomes the <c>[image: name]</c> label a terminal without pictures shows, and the span
    /// carries the request to draw it. Inside a link it keeps the link's action; on its own it opens the picture. A
    /// picture the server left cells for (sized in cells both ways) is drawn in them: its first row's blanks become
    /// the label, cut to fit, and the rows under it stay the blanks they are.
    /// </summary>
    private static SpanInteraction? Picture(ImageMarkup image, SpanInteraction? enclosing, ref string text)
    {
        if (image.Row is { } row)
        {
            if (row.Row > 0)
            {
                return enclosing;
            }

            var url = MxpImageSource.Resolve(image.Source, null, null);
            var name = MxpImageSource.DisplayName(image.Source);
            var label = $"[image: {name}]";
            var cells = text.Length;
            label = label.Length <= cells ? label.PadRight(cells) : cells > 1 ? label[..(cells - 1)] + "…" : label[..cells];
            text = label;
            if (url is null)
            {
                return enclosing;
            }

            var reserved = new InlineImageRequest(url, name, new ImageExtent(row.Columns, ImageExtentUnit.Characters),
                new ImageExtent(row.Rows, ImageExtentUnit.Characters)) { Reserved = true };
            return (enclosing ?? SpanInteraction.Link(url)) with { Image = reserved };
        }

        var source = MxpImageSource.Resolve(image.Source, null, null);
        var display = MxpImageSource.DisplayName(string.IsNullOrWhiteSpace(text) ? image.Source : text);
        text = $"[image: {display}]";
        if (source is null)
        {
            return enclosing;
        }

        ImageExtent? width = image.Width is > 0 and <= ImageExtent.MaxValue ? new ImageExtent(image.Width.Value, ImageExtentUnit.Pixels) : null;
        ImageExtent? height = image.Height is > 0 and <= ImageExtent.MaxValue ? new ImageExtent(image.Height.Value, ImageExtentUnit.Pixels) : null;
        var request = new InlineImageRequest(source, display, width, height);
        return (enclosing ?? SpanInteraction.Link(source)) with { Image = request };
    }

    private static TerminalColor Color(AnsiColor? color) => color switch
    {
        AnsiColor.Standard standard => TerminalColor.FromIndex(standard.Index + (standard.Bright ? 8 : 0)),
        AnsiColor.Xterm xterm => TerminalColor.FromIndex(xterm.Index),
        AnsiColor.Rgb rgb => TerminalColor.FromRgb(rgb.R, rgb.G, rgb.B),
        _ => TerminalColor.Default,
    };

    private static TextAttributes Attributes(AnsiStyle style)
    {
        var attributes = TextAttributes.None;
        if (style.Bold)
        {
            attributes |= TextAttributes.Bold;
        }

        if (style.Faint)
        {
            attributes |= TextAttributes.Faint;
        }

        if (style.Italic)
        {
            attributes |= TextAttributes.Italic;
        }

        if (style.Underlined)
        {
            attributes |= TextAttributes.Underline;
        }

        if (style.Blink)
        {
            attributes |= TextAttributes.Blink;
        }

        if (style.Inverted)
        {
            attributes |= TextAttributes.Reverse;
        }

        if (style.Concealed)
        {
            attributes |= TextAttributes.Conceal;
        }

        if (style.StrikeThrough)
        {
            attributes |= TextAttributes.Strikethrough;
        }

        return attributes;
    }

    /// <summary>A frame's name as a window: one of this connection's few, or null for the main window.</summary>
    private string? FrameName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxFrameNameLength ||
            name.Equals("_top", StringComparison.OrdinalIgnoreCase) || name.Equals("_previous", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_frames.Contains(name))
        {
            return name;
        }

        if (_frames.Count >= MaxFrames)
        {
            return null;
        }

        _frames.Add(name);
        return name;
    }

    // ── Flags ────────────────────────────────────────────────────────────────────

    private void Flag(MxpFlagMarkup flag, string text)
    {
        if (!ReferenceEquals(flag, _flag))
        {
            EndFlags(null, lineEnd: false);
            _flag = flag;
        }

        _flagText.Append(text);
    }

    /// <summary>
    /// Ends the flagged element being read when it is over: at a line end where <paramref name="seen"/> (the flag the
    /// line ended in) is another one, or the element is no longer open, or when another begins. One left open at the line end goes on to the next line.
    /// </summary>
    private void EndFlags(MxpFlagMarkup? seen, bool lineEnd)
    {
        if (_flag is not { } flag)
        {
            return;
        }

        if (lineEnd && (seen is null || ReferenceEquals(seen, flag)) && _decoder.OpenElements.Contains(flag))
        {
            // Still open: a secure element spans lines, and its text keeps them. A blank line inside it has no
            // flagged text (seen is null) and is kept too.
            _flagText.Append('\n');
            return;
        }

        var text = _flagText.ToString().TrimEnd('\n');
        _flagText.Clear();
        _flag = null;
        var name = flag.Flag;
        if (name.StartsWith("set ", StringComparison.OrdinalIgnoreCase) || name.StartsWith("private ", StringComparison.OrdinalIgnoreCase))
        {
            var variable = name[(name.IndexOf(' ', StringComparison.Ordinal) + 1)..].Trim();
            if (variable.Length > 0 && !name.StartsWith("private ", StringComparison.OrdinalIgnoreCase))
            {
                SetEntity(variable, text);
            }

            return;
        }

        Flagged?.Invoke(this, new MxpFlagText(name, text));
    }

    private void SetEntity(string name, string? value)
    {
        if (value is null)
        {
            _entities.Remove(name);
        }
        else
        {
            _entities[name] = value;
        }

        EntityChanged?.Invoke(this, new MxpEntityChange(name, value));
    }

    /// <summary>Claims a tab and writes it as it is, for the session to expand.</summary>
    private sealed class LiteralTabs : IDecoderPlugin
    {
        public string Name => "sharpmuterm-tabs";

        public void Register(DecoderBuilder builder) => builder.OnControl('\t', static (_, context) =>
        {
            context.Write("\t");
            return true;
        });
    }

    // ── Events ───────────────────────────────────────────────────────────────────

    private void OnEvent(DecodeEvent e)
    {
        switch (e)
        {
            case ReplyEvent reply:
                ClientReply?.Invoke(this, reply.Text.TrimEnd('\r', '\n'));
                break;
            case VariableEvent variable:
                SetEntity(variable.Name, variable.Value);
                break;
            case GaugeEvent gauge:
                GaugeDeclared?.Invoke(this, new MxpGauge(gauge.Variable, gauge.Maximum,
                    gauge.Caption is { Length: > 0 } caption ? caption[..Math.Min(caption.Length, MaxCaptionLength)] : null, gauge.Status));
                break;
        }
    }
}
