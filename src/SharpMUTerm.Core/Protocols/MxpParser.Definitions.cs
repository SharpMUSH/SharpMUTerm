using System.Text;
using SharpMUTerm.Core.Media;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Protocols;

/// <summary>A server's entity changed: <c>&lt;!ENTITY&gt;</c>, <c>&lt;VAR&gt;</c> or an element's <c>FLAG="set x"</c>. A null value is a deletion.</summary>
public sealed record MxpEntityChange(string Name, string? Value);

/// <summary>The text of an element whose <c>FLAG</c> says what it is: <c>RoomName</c>, <c>RoomDesc</c>, <c>RoomExit</c>, <c>RoomNum</c>…</summary>
public sealed record MxpFlagText(string Flag, string Text);

/// <summary>A <c>&lt;GAUGE&gt;</c> or <c>&lt;STAT&gt;</c>: show entity <see cref="Entity"/> (out of <see cref="Max"/>) on the status row.</summary>
public sealed record MxpGauge(string Entity, string? Max, string? Caption, bool IsStat);

/// <summary>A <c>&lt;FRAME&gt;</c> the server opened or closed.</summary>
public sealed record MxpFrame(string Name, string? Title, bool Open);

/// <summary>Text a <c>&lt;DEST&gt;</c> or a redirecting <c>&lt;FRAME&gt;</c> sent to a named window instead of the main one.</summary>
public sealed record MxpRedirect(string Frame, StyledLine Line);

/// <summary>
/// What a server defines and declares over MXP: <c>&lt;!ELEMENT&gt;</c> and <c>&lt;!ENTITY&gt;</c>, <c>&lt;VAR&gt;</c>,
/// <c>&lt;GAUGE&gt;</c>/<c>&lt;STAT&gt;</c>, <c>&lt;SOUND&gt;</c>/<c>&lt;MUSIC&gt;</c>, and <c>&lt;FRAME&gt;</c>/<c>&lt;DEST&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every one of these is a secure tag</b>, so on an open line it is echoed as text like any other — a player
/// cannot define an element, set an entity or open a window. Definitions are parser state, so they last for the
/// connection and a reconnect, which builds a new parser, starts without them.
/// </para>
/// <para>
/// <b>An element's own tags inherit the trust it was opened with.</b> One opened on a secure line may contain
/// anything; one a player used on an open line, because the server defined it <c>OPEN</c>, may contain only
/// open tags, and anything else in its definition is dropped rather than echoed (the definition is the server's
/// text, not the player's). Attribute values are substituted with <c>&lt;</c> and <c>&gt;</c> taken out, so a
/// value cannot close the tag it lands in and open another.
/// </para>
/// <para>
/// <b>Definitions are bounded</b> so one cannot be used as a bomb: how many elements and entities a connection
/// may hold, how long a definition or value may be, how deep elements may nest inside one another, and how many
/// expansions a single line may cost.
/// </para>
/// </remarks>
public sealed partial class MxpParser
{
    /// <summary>The most elements one connection may define.</summary>
    public const int MaxElements = 256;

    /// <summary>The most entities one connection may define.</summary>
    public const int MaxEntities = 512;

    /// <summary>The longest element definition or entity value kept.</summary>
    public const int MaxDefinitionLength = 1024;

    /// <summary>How deep an element may open other elements.</summary>
    public const int MaxExpansionDepth = 4;

    /// <summary>How many element expansions one line may cost.</summary>
    public const int MaxExpansionsPerLine = 64;

    /// <summary>The most windows one connection's frames may name.</summary>
    public const int MaxFrames = 8;

    private const int MaxNameLength = 32;

    /// <summary>Why a tag was allowed, which an element's own tags inherit.</summary>
    private enum Trust
    {
        None,
        Open,
        Secure,
    }

    private sealed record Element(
        string Name,
        string Definition,
        IReadOnlyList<(string Name, string? Default)> Attributes,
        string? Flag,
        int? LineTag,
        bool Open,
        bool Empty);

    private readonly Dictionary<string, Element> _elements = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _entities = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _frames = new(StringComparer.OrdinalIgnoreCase);
    private string? _redirect;
    private bool _markupThisLine;
    private int _expansionDepth;
    private int _expansionsThisLine;

    /// <summary>Raised when a server's entity is set or deleted. Not for one defined <c>PRIVATE</c>.</summary>
    public event EventHandler<MxpEntityChange>? EntityChanged;

    /// <summary>Raised when a flagged element closes, with its text.</summary>
    public event EventHandler<MxpFlagText>? Flagged;

    /// <summary>Raised for each <c>&lt;GAUGE&gt;</c> or <c>&lt;STAT&gt;</c>.</summary>
    public event EventHandler<MxpGauge>? GaugeDeclared;

    /// <summary>Raised for each <c>&lt;SOUND&gt;</c> or <c>&lt;MUSIC&gt;</c>, as the same request MSP and MCMP make.</summary>
    public event EventHandler<MediaCommand>? MediaRequested;

    /// <summary>Raised when a <c>&lt;FRAME&gt;</c> opens or closes a window.</summary>
    public event EventHandler<MxpFrame>? FrameDeclared;

    /// <summary>Raised with text a <c>&lt;DEST&gt;</c> or a redirect sent to another window.</summary>
    public event EventHandler<MxpRedirect>? Redirected;

    /// <summary>The value of a server entity, or null. For tests and the inspector.</summary>
    public string? Entity(string name) => _entities.GetValueOrDefault(name);

    /// <summary>How many elements are defined.</summary>
    public int ElementCount => _elements.Count;

    private void ResetDefinitions()
    {
        _elements.Clear();
        _entities.Clear();
        _frames.Clear();
        _redirect = null;
        _markupThisLine = false;
        _expansionDepth = 0;
        _expansionsThisLine = 0;
        LineSwallowed = false;
    }

    /// <summary>The names no server may redefine: every tag this parser handles itself, and their spellings.</summary>
    private static bool IsBuiltIn(string name) =>
        MxpTagCategory.IsOpen(name) || Canonical(name) is "B" or "I" or "U" or "S" or "COLOR" or "FONT" or "SEND" or "A"
            or "BR" or "VERSION" or "SUPPORT" or "IMAGE" or "!ELEMENT" or "!ENTITY" or "VAR" or "GAUGE" or "STAT"
            or "SOUND" or "MUSIC" or "FRAME" or "DEST" or "SUPPORTS" or "EXPIRE" or "RESET" or "P" or "NOBR";

    private static bool IsName(string? name) =>
        name is { Length: > 0 and <= MaxNameLength } && char.IsLetter(name[0])
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    // ----- <!ELEMENT> --------------------------------------------------------

    /// <summary><c>&lt;!ELEMENT name 'definition' [ATT='a b=default'] [TAG=n] [FLAG=flag] [OPEN] [DELETE] [EMPTY]&gt;</c>.</summary>
    private void DefineElement(string attrs)
    {
        var parsed = ParseAttributes(attrs);
        var name = Positional(parsed, 0);
        if (!IsName(name) || IsBuiltIn(name!))
        {
            return;
        }

        if (HasFlag(parsed, "DELETE"))
        {
            _elements.Remove(name!);
            return;
        }

        var definition = Positional(parsed, 1) ?? string.Empty;
        if (definition.Length > MaxDefinitionLength || (!_elements.ContainsKey(name!) && _elements.Count >= MaxElements))
        {
            return;
        }

        var attributes = (GetAttr(parsed, "ATT") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(16)
            .Select(a => a.Split('=', 2) is var kv && kv.Length == 2 ? (kv[0], (string?)kv[1]) : (a, null))
            .Where(a => IsName(a.Item1))
            .ToArray();
        int? lineTag = int.TryParse(GetAttr(parsed, "TAG"), out var tag) && tag is >= 20 and <= 99 ? tag : null;
        var flag = GetAttr(parsed, "FLAG") is { Length: > 0 and <= 64 } f ? f : null;

        _elements[name!] = new Element(
            Canonical(name!), definition, attributes, flag, lineTag, HasFlag(parsed, "OPEN"), HasFlag(parsed, "EMPTY"));
    }

    /// <summary>
    /// Opens an element: a frame of its own, then the tags of its definition with its attributes filled in.
    /// Its closing tag closes them all, because their frames sit above its own on the stack.
    /// </summary>
    private void OpenElement(Element element, string attrs, Trust trust, bool endsWithLine)
    {
        if (_expansionDepth >= MaxExpansionDepth || _expansionsThisLine >= MaxExpansionsPerLine)
        {
            return;
        }

        _expansionsThisLine++;
        var values = ElementValues(element, ParseAttributes(attrs));
        var start = _stack.Count;
        PushFrame(
            element.Name, isInteraction: false, isLink: false, deferCommand: false, hint: null, promptOnly: false,
            flag: element.Empty ? null : element.Flag, endsWithLine: endsWithLine); // an empty element has no text to flag

        _expansionDepth++;
        try
        {
            foreach (var tag in DefinitionTags(Substitute(element.Definition, values)))
            {
                ProcessInnerTag(tag, trust);
            }
        }
        finally
        {
            _expansionDepth--;
        }

        if (element.Empty)
        {
            FlushRun();
            CloseFramesFrom(start);
        }
    }

    /// <summary>What each of an element's attributes is this time: given by name, by position, or its default.</summary>
    private static Dictionary<string, string> ElementValues(Element element, List<(string? Key, string Value)> given)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < element.Attributes.Count; i++)
        {
            var (name, fallback) = element.Attributes[i];
            var value = GetAttr(given, name) ?? Positional(given, i) ?? fallback ?? string.Empty;
            values[name] = value.Replace("<", string.Empty, StringComparison.Ordinal).Replace(">", string.Empty, StringComparison.Ordinal);
        }

        return values;
    }

    /// <summary>The definition with each <c>&amp;attribute;</c> replaced; any other entity is left for the tag to read.</summary>
    private static string Substitute(string definition, Dictionary<string, string> values)
    {
        if (values.Count == 0 || !definition.Contains('&', StringComparison.Ordinal))
        {
            return definition;
        }

        var sb = new StringBuilder(definition.Length);
        var i = 0;
        while (i < definition.Length)
        {
            var semi = definition[i] == '&' ? definition.IndexOf(';', i + 1) : -1;
            if (semi > i && values.TryGetValue(definition[(i + 1)..semi], out var value))
            {
                sb.Append(value);
                i = semi + 1;
                if (sb.Length > MaxDefinitionLength * 2)
                {
                    break;
                }

                continue;
            }

            sb.Append(definition[i++]);
        }

        return sb.ToString();
    }

    /// <summary>The tag bodies in a definition, in order. Text between them is not part of an element.</summary>
    private static IEnumerable<string> DefinitionTags(string definition)
    {
        var i = 0;
        while ((i = definition.IndexOf('<', i)) >= 0)
        {
            var end = i + 1;
            char? quote = null;
            while (end < definition.Length && (quote is not null || definition[end] != '>'))
            {
                if (definition[end] is '"' or '\'')
                {
                    quote = quote == definition[end] ? null : quote ?? definition[end];
                }

                end++;
            }

            if (end >= definition.Length)
            {
                yield break;
            }

            yield return definition[(i + 1)..end];
            i = end + 1;
        }
    }

    /// <summary>
    /// One tag of an element's definition. It inherits the element's trust rather than asking the line, and a
    /// tag that trust does not allow is dropped, not echoed. Closing tags in a definition are ignored: the
    /// element's own closing tag closes everything it opened.
    /// </summary>
    private void ProcessInnerTag(string raw, Trust trust)
    {
        var trimmed = raw.Trim().TrimEnd('/').TrimEnd();
        if (trimmed.Length == 0 || trimmed[0] == '/')
        {
            return;
        }

        var nameEnd = 0;
        while (nameEnd < trimmed.Length && !char.IsWhiteSpace(trimmed[nameEnd]))
        {
            nameEnd++;
        }

        var name = Canonical(trimmed[..nameEnd]);
        var allowed = trust == Trust.Secure
            || MxpTagCategory.IsOpen(name)
            || (_elements.TryGetValue(name, out var inner) && inner.Open);

        // A definition never defines: an element that wrote <!ELEMENT> into itself would grow the table each use.
        if (!allowed || name is "!ELEMENT" or "!ENTITY")
        {
            return;
        }

        Open(name, nameEnd < trimmed.Length ? trimmed[(nameEnd + 1)..] : string.Empty, trust);
    }

    /// <summary>
    /// <c>ESC[#z</c> for 10–12 and 20–99: the line's text is a room name, description or exits (the spec's own
    /// numbers), or an element the server tied to the number with <c>TAG=</c>. Either way it ends with the line.
    /// Line tags come only from the server, so the element opens with secure trust.
    /// </summary>
    private void OpenLineTagElement(int tag)
    {
        if (tag is >= 10 and <= 12)
        {
            FlushRun();
            PushFrame(
                "LINETAG", isInteraction: false, isLink: false, deferCommand: false, hint: null, promptOnly: false,
                flag: tag switch { 10 => "RoomName", 11 => "RoomDesc", _ => "RoomExit" }, endsWithLine: true);
            return;
        }

        if (_elements.Values.FirstOrDefault(e => e.LineTag == tag) is { } element)
        {
            FlushRun();
            OpenElement(element, string.Empty, Trust.Secure, endsWithLine: true);
        }
    }

    // ----- <!ENTITY>, <VAR> and FLAG ---------------------------------------------

    /// <summary><c>&lt;!ENTITY name value [DESC=] [PRIVATE] [PUBLISH] [DELETE] [ADD] [REMOVE]&gt;</c>.</summary>
    private void DefineEntity(string attrs)
    {
        var parsed = ParseAttributes(attrs);
        var name = Positional(parsed, 0);
        if (!IsName(name) || ResolveEntity(name!) is not null || string.Equals(name, "text", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var priv = HasFlag(parsed, "PRIVATE");
        if (HasFlag(parsed, "DELETE"))
        {
            SetEntity(name!, null, priv);
            return;
        }

        var value = Positional(parsed, 1) ?? string.Empty;
        var held = _entities.GetValueOrDefault(name!);
        if (HasFlag(parsed, "ADD"))
        {
            value = string.IsNullOrEmpty(held) ? value : held + "|" + value;
        }
        else if (HasFlag(parsed, "REMOVE"))
        {
            value = string.Join('|', (held ?? string.Empty).Split('|').Where(v => v != value));
        }

        SetEntity(name!, value, priv);
    }

    /// <summary>Sets or deletes an entity, within the caps, and says so unless it is private.</summary>
    private void SetEntity(string name, string? value, bool isPrivate)
    {
        if (value is null)
        {
            if (_entities.Remove(name) && !isPrivate)
            {
                EntityChanged?.Invoke(this, new MxpEntityChange(name, null));
            }

            return;
        }

        if (value.Length > MaxDefinitionLength || (!_entities.ContainsKey(name) && _entities.Count >= MaxEntities))
        {
            return;
        }

        _entities[name] = value;
        if (!isPrivate)
        {
            EntityChanged?.Invoke(this, new MxpEntityChange(name, value));
        }
    }

    /// <summary>A server entity's value for <c>&amp;name;</c> in text, or null. Built-in entities are read first.</summary>
    private string? UserEntity(string name) => _entities.GetValueOrDefault(name);

    /// <summary><c>&lt;VAR name [PRIVATE]&gt;text&lt;/VAR&gt;</c>: the text is shown, and stored as the entity.</summary>
    private void OpenVar(string attrs)
    {
        var parsed = ParseAttributes(attrs);
        if (Positional(parsed, 0) is not { } name || !IsName(name))
        {
            return;
        }

        PushFrame(
            "VAR", isInteraction: false, isLink: false, deferCommand: false, hint: null, promptOnly: false,
            flag: (HasFlag(parsed, "PRIVATE") ? "private " : "set ") + name);
    }

    /// <summary>
    /// What a flagged or redirected frame does with its text: at a line boundary it banks this line's share (or,
    /// for a <c>&lt;DEST&gt;</c>, sends it); on <paramref name="closing"/> it finishes.
    /// </summary>
    private void CompleteDefinitionFrame(Frame frame, bool closing)
    {
        if (frame.Destination is { } destination)
        {
            FlushRun();
            if (frame.SpanStart < _lineSpans.Count)
            {
                var spans = _lineSpans.GetRange(frame.SpanStart, _lineSpans.Count - frame.SpanStart);
                _lineSpans.RemoveRange(frame.SpanStart, spans.Count);
                Redirected?.Invoke(this, new MxpRedirect(destination, new StyledLine(spans)));
            }

            return;
        }

        if (frame.Flag is not { } flag || frame.Captured is not { } captured)
        {
            return;
        }

        FlushRun();
        for (var i = frame.SpanStart; i < _lineSpans.Count; i++)
        {
            captured.Append(_lineSpans[i].Text);
        }

        if (!closing)
        {
            captured.Append('\n');
            return;
        }

        var text = captured.ToString().TrimEnd('\n');
        captured.Clear();
        if (flag.StartsWith("set ", StringComparison.OrdinalIgnoreCase) || flag.StartsWith("private ", StringComparison.OrdinalIgnoreCase))
        {
            var space = flag.IndexOf(' ', StringComparison.Ordinal);
            var name = flag[(space + 1)..].Trim();
            if (IsName(name))
            {
                SetEntity(name, text, isPrivate: flag[0] is 'p' or 'P');
            }

            return;
        }

        Flagged?.Invoke(this, new MxpFlagText(flag, text));
    }

    // ----- <GAUGE>, <SOUND>, <FRAME>, <DEST> ---------------------------------------

    /// <summary><c>&lt;GAUGE entity [MAX=entity] [CAPTION=text] [COLOR=colour]&gt;</c>, and <c>&lt;STAT&gt;</c> the same.</summary>
    private void DeclareGauge(string attrs, bool isStat)
    {
        var parsed = ParseAttributes(attrs);
        var entity = GetAttr(parsed, "ENTITY") ?? Positional(parsed, 0);
        var max = GetAttr(parsed, "MAX") ?? Positional(parsed, 1);
        if (!IsName(entity) || (max is not null && !IsName(max)))
        {
            return;
        }

        var caption = GetAttr(parsed, "CAPTION") ?? Positional(parsed, 2);
        GaugeDeclared?.Invoke(this, new MxpGauge(entity!, max, caption is { Length: > 0 } ? caption[..Math.Min(caption.Length, 64)] : null, isStat));
    }

    /// <summary><c>&lt;SOUND fname V= L= P= T= U=&gt;</c> and <c>&lt;MUSIC fname V= L= C= T= U=&gt;</c>, MSP's fields as attributes.</summary>
    private void RequestMedia(MediaKind kind, string attrs)
    {
        var parsed = ParseAttributes(attrs);
        if ((GetAttr(parsed, "FNAME") ?? Positional(parsed, 0)) is not { Length: > 0 and <= 256 } name)
        {
            return;
        }

        var fields = new Dictionary<char, string>();
        foreach (var (key, value) in parsed)
        {
            if (key is { Length: 1 })
            {
                fields[char.ToUpperInvariant(key[0])] = value;
            }
        }

        MediaRequested?.Invoke(this, MspTrigger.FromFields(kind, name, fields));
    }

    /// <summary>A frame's name as a window: one of this connection's few, or null for the main window.</summary>
    private string? FrameName(string? name)
    {
        if (name is null || name.Equals("_top", StringComparison.OrdinalIgnoreCase) || name.Equals("_previous", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!IsName(name))
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

    /// <summary>
    /// <c>&lt;FRAME name [ACTION=OPEN|CLOSE|REDIRECT] [TITLE=…]&gt;</c>. A frame is a window; OPEN asks for one,
    /// CLOSE closes it, and REDIRECT sends every following line there until a redirect to <c>_top</c>.
    /// Layout attributes (ALIGN, LEFT, WIDTH…) are read and ignored: where a window goes is the player's.
    /// </summary>
    private void DeclareFrame(string attrs)
    {
        var parsed = ParseAttributes(attrs);
        var raw = GetAttr(parsed, "NAME") ?? Positional(parsed, 0);
        var action = (GetAttr(parsed, "ACTION") ?? (HasFlag(parsed, "REDIRECT") ? "REDIRECT" : "OPEN")).ToUpperInvariant();
        if (action == "REDIRECT")
        {
            _redirect = FrameName(raw);
            return;
        }

        if (FrameName(raw) is not { } name)
        {
            return;
        }

        if (action == "CLOSE")
        {
            _frames.Remove(name);
            if (string.Equals(_redirect, name, StringComparison.OrdinalIgnoreCase))
            {
                _redirect = null;
            }
        }

        var title = GetAttr(parsed, "TITLE") is { Length: > 0 } t ? t[..Math.Min(t.Length, 64)] : null;
        FrameDeclared?.Invoke(this, new MxpFrame(name, title, action != "CLOSE"));
    }

    /// <summary><c>&lt;DEST name&gt;text&lt;/DEST&gt;</c>: the text goes to that window and not to this line.</summary>
    private void OpenDest(string attrs)
    {
        var parsed = ParseAttributes(attrs);
        var name = FrameName(GetAttr(parsed, "NAME") ?? Positional(parsed, 0));

        // A DEST to the main window, or past the cap, is a formatting no-op: its text stays where it is.
        PushFrame("DEST", isInteraction: false, isLink: false, deferCommand: false, hint: null, promptOnly: false, destination: name);
    }
}
