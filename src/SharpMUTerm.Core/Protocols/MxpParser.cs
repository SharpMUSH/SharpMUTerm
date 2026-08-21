using System.Globalization;
using System.Text;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Protocols;

/// <summary>
/// Incremental, stateful, line-oriented parser for the MUD eXtension Protocol (MXP).
/// Feed it decoded text (any number of chunks); it emits fully-terminated
/// <see cref="StyledLine"/>s and retains the in-progress line, the current
/// <see cref="TextStyle"/>, and the stack of open tags across calls, so a tag
/// (<c>&lt;...&gt;</c>) or entity (<c>&amp;...;</c>) may straddle a chunk boundary.
///
/// Mirrors the shape of <see cref="AnsiParser"/>: <see cref="Feed(string)"/> returns the
/// lines completed within the chunk, <see cref="Flush"/> yields a buffered partial line
/// (e.g. an unterminated prompt), and colour/attribute state is preserved between calls.
///
/// <para>
/// <b><see cref="Flush"/> is the line boundary, not <c>'\n'</c>.</b> The telnet layer strips the
/// terminator before this parser sees it, so <c>WorldSession</c> feeds a line's text and then flushes;
/// a rule keyed on a newline in the text never fires on a real connection. <see cref="EndLine"/> is
/// the one place the end of a line is defined, and both routes go through it.
/// </para>
///
/// <para>Scope notes for v1:</para>
/// <list type="bullet">
/// <item>The line-security model (<c>ESC[#z</c> line tags, open/secure/locked, RESET, TEMP SECURE
/// and the three LOCK modes) <b>is</b> implemented — see <see cref="MxpLineMode"/>. A secure tag on
/// an open line is emitted as literal text rather than honoured, which is what stops a player's
/// <c>&lt;SEND&gt;</c> becoming a clickable command in someone else's client.</item>
/// <item>Inline <c>&lt;IMG&gt;</c> rendering is out of scope (graphics live elsewhere); the
/// tag is parsed and ignored gracefully.</item>
/// <item>ANSI SGR is decoded here, through <see cref="SharpMUTerm.Core.Text.SgrCodes"/>, because the
/// spec permits ANSI inside MXP and nothing upstream strips it — a session runs this parser
/// <em>or</em> <see cref="SharpMUTerm.Core.Text.AnsiParser"/>, never both. Other CSI sequences are
/// consumed and discarded.</item>
/// <item>Unknown/unsupported tags (<c>&lt;VAR&gt;</c>, <c>&lt;EXPIRE&gt;</c>, <c>&lt;H1&gt;</c>,
/// <c>&lt;P&gt;</c>, …) are consumed and discarded on a line allowed to use them. They are all
/// secure tags, so on an open line they are echoed literally like any other refused tag rather
/// than silently dropped.</item>
/// <item>A stray <c>&lt;</c> that cannot begin a tag, or a stray <c>&amp;</c> that cannot begin
/// an entity, is emitted literally (xterm-like leniency). Tag/entity buffers are length
/// capped to avoid runaway on malformed input.</item>
/// </list>
/// </summary>
public sealed class MxpParser : ILineParser
{
    private const int MaxTagLength = 4096;
    private const int MaxEntityLength = 32;

    private enum Mode
    {
        Text,
        Tag,
        Entity,
        Escape,
        EscapeIntermediate,
        Csi,
        Osc,
        OscEscape,
    }

    private const int MaxSequenceLength = 128;

    /// <summary>A single open MXP element on the tag stack.</summary>
    private sealed class Frame
    {
        public required string Name { get; init; }
        public TextStyle SavedStyle { get; init; }
        public SpanInteraction? SavedInteraction { get; init; }
        public bool IsInteraction { get; init; }
        public bool IsLink { get; init; }
        public bool DeferCommand { get; init; }
        public string? Hint { get; init; }
        public bool PromptOnly { get; init; }
        public int SpanStart { get; init; }

        /// <summary>
        /// True when the element was opened while the line was in <see cref="MxpLineMode.Open"/> —
        /// i.e. it is one of the spec's "tags that were used while in open mode", the only ones it
        /// closes automatically. Secure tags are "never automatically closed", so this is what keeps
        /// a server's own markup spanning lines while a player's is bounded by theirs.
        /// </summary>
        public bool OpenedInOpenMode { get; init; }
    }

    private Mode _mode = Mode.Text;

    /// <summary>The mode a line starts in; moved only by the three LOCK line tags and by RESET.</summary>
    private MxpLineMode _defaultMode = MxpLineMode.Open;

    /// <summary>The mode in force right now; reverts to <see cref="_defaultMode"/> at each newline.</summary>
    private MxpLineMode _lineMode = MxpLineMode.Open;

    /// <summary>Set by TEMP SECURE (<c>ESC[4z</c>) and consumed by the very next tag.</summary>
    private bool _tempSecure;

    private TextStyle _current = TextStyle.Default;
    private SpanInteraction? _interaction;
    private readonly StringBuilder _run = new();
    private readonly StringBuilder _tag = new();
    private readonly StringBuilder _entity = new();
    private readonly StringBuilder _seq = new();
    private readonly List<StyledSpan> _lineSpans = new();
    private readonly List<Frame> _stack = new();
    private List<StyledLine>? _emit;

    /// <summary>The rendition state that will apply to the next printed character.</summary>
    public TextStyle CurrentStyle => _current;

    /// <summary>
    /// A line this parser owes the server — the answer to a <c>&lt;VERSION&gt;</c> or
    /// <c>&lt;SUPPORT&gt;</c> request. An event rather than a return value because it is not output:
    /// nothing about it belongs in the scrollback, and the session sends it as a line.
    /// </summary>
    public event EventHandler<string>? ClientReply;

    /// <summary>What this client answers a VERSION request with.</summary>
    private const string ClientName = "SharpMUTerm";

    /// <summary>
    /// Spec: "the response is sent as a secure-tagged line: <c>&lt;ESC&gt;[1z</c> prefix with newline
    /// suffix, ensuring the MUD recognizes it as client-generated rather than player-controlled input."
    /// Only the prefix belongs here — <c>WorldSession.SendRawAsync</c>'s send path
    /// (<c>TelnetSession.SendLineAsync</c>, whose own doc comment says the terminator is the library's
    /// to add) already appends the line terminator, so adding one here would double it, exactly the bug
    /// that doc comment warns against for a plain command.
    /// </summary>
    private const string SecureLinePrefix = "\x1b[1z";

    /// <summary>
    /// The tags this parser genuinely implements, in the spec's <c>+tag</c> form.
    /// </summary>
    /// <remarks>
    /// An honest list, and deliberately not an aspirational one: a SUPPORTS answer is a claim a
    /// server acts on, so naming a tag we ignore makes it send markup we will render as text. Add to
    /// this only when the handler exists — the same rule the MTTS bit vector is held to.
    /// <c>H</c>/<c>HIGH</c> is on <see cref="MxpTagCategory"/>'s open allow-list but
    /// <see cref="HandleOpener"/> has no case for it — it falls to the default and is silently
    /// dropped — so it is deliberately not claimed here.
    /// </remarks>
    private static readonly string[] SupportedTags =
        ["+b", "+i", "+u", "+s", "+color", "+font", "+send", "+a", "+br"];

    /// <summary>True when a partial line, an open tag/entity, or unclosed markup is buffered.</summary>
    public bool HasPendingContent =>
        _run.Length > 0 || _lineSpans.Count > 0 || _mode != Mode.Text;

    /// <summary>Feeds a chunk of text, returning every line completed by a newline (or <c>&lt;BR&gt;</c>) within it.</summary>
    public IReadOnlyList<StyledLine> Feed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Feed(text.AsSpan());
    }

    /// <summary>Feeds a chunk of text, returning every line completed by a newline (or <c>&lt;BR&gt;</c>) within it.</summary>
    public IReadOnlyList<StyledLine> Feed(ReadOnlySpan<char> text)
    {
        _emit = null;
        foreach (var ch in text)
        {
            Process(ch);
        }

        var result = (IReadOnlyList<StyledLine>?)_emit ?? Array.Empty<StyledLine>();
        _emit = null;
        return result;
    }

    /// <summary>
    /// Returns the buffered partial line (e.g. a prompt not terminated by a newline) and clears it,
    /// or <c>null</c> if nothing is buffered.
    /// </summary>
    /// <remarks>
    /// <b>This is the line boundary</b>, not just a drain: the telnet layer strips the terminator, so
    /// on a real connection a line arrives as <c>Feed(text)</c> with no <c>'\n'</c> in it followed by
    /// this call. Everything the end of a line does — the security mode reverting to the default, the
    /// spec's auto-close of open-mode tags, finalising an open interaction, abandoning an unterminated
    /// escape sequence — therefore happens here. See <see cref="EndLine"/>.
    /// </remarks>
    public StyledLine? Flush()
    {
        EndLine();
        if (_lineSpans.Count == 0)
        {
            return null;
        }

        var line = new StyledLine(_lineSpans);
        _lineSpans.Clear();
        return line;
    }

    /// <summary>Resets all parser state, including the current style and the open-tag stack.</summary>
    public void Reset()
    {
        _mode = Mode.Text;
        _defaultMode = MxpLineMode.Open;
        _lineMode = MxpLineMode.Open;
        _tempSecure = false;
        _current = TextStyle.Default;
        _interaction = null;
        _run.Clear();
        _tag.Clear();
        _entity.Clear();
        _seq.Clear();
        _lineSpans.Clear();
        _stack.Clear();
    }

    private void Process(char ch)
    {
        // A newline ends the line whatever sequence is in flight. Without this an unterminated
        // string escape — a bare ESC ] , ESC P, ESC X, ESC ^ or ESC _ , which a *player* can type
        // into a public channel — consumes every following character, line boundaries included,
        // until a BEL or ST it need never send: the rest of the session's output disappears. CSI
        // already treats a control byte as malformed, and a two-byte escape would otherwise swallow
        // the newline as its second byte; routing all of them through here makes one rule of it.
        // AnsiParser carries the same three lines — the two escape state machines are near
        // duplicates and are deliberately not consolidated, so a fix to one belongs in both.
        if (ch == '\n' && IsEscapeMode(_mode))
        {
            EndSequence();
        }

        switch (_mode)
        {
            case Mode.Text:
                ProcessText(ch);
                break;

            case Mode.Tag:
                ProcessTagChar(ch);
                break;

            case Mode.Entity:
                ProcessEntityChar(ch);
                break;

            case Mode.Escape:
                ProcessEscape(ch);
                break;

            case Mode.EscapeIntermediate:
                // Consume the single trailing byte of e.g. ESC ( B and return to text.
                EndSequence();
                break;

            case Mode.Csi:
                ProcessCsi(ch);
                break;

            case Mode.Osc:
                ProcessOsc(ch);
                break;

            case Mode.OscEscape:
                // Inside an OSC string we saw ESC; a following '\' is the ST terminator.
                if (ch == '\\')
                {
                    EndSequence();
                }
                else
                {
                    _mode = Mode.Osc;
                }

                break;
        }
    }

    /// <summary>True while the parser is part-way through an escape sequence rather than reading text.</summary>
    private static bool IsEscapeMode(Mode mode) =>
        mode is Mode.Escape or Mode.EscapeIntermediate or Mode.Csi or Mode.Osc or Mode.OscEscape;

    private void ProcessText(char ch)
    {
        // Spec: TEMP SECURE "must be immediately followed by a '<' character to start a tag." That
        // "immediately" is the whole of it — an arming that survives the server's own prose is spent
        // by the first tag a *player* wrote into that prose, and
        // "\x1b[4zRivane says, '<SEND HREF=\"@shutdown\">click</SEND>'" becomes a clickable
        // @shutdown on a line the server never secured.
        //
        // ESC is the sole exception, because it may still be resolving a line tag; every escape that
        // turns out to be something else disarms for itself in <see cref="EndSequence"/>. On a locked
        // line no character starts a tag, not even '<', so there every character disarms — which is
        // what closes the ESC[7z … ESC[4z … ESC[0z route through the unlock.
        if (_tempSecure && ch != '\x1b' && (ch != '<' || _lineMode == MxpLineMode.Locked))
        {
            _tempSecure = false;
        }

        // A locked line is not parsed: "no MXP or HTML commands are allowed in the line. The line is
        // not parsed for any tags at all." Newline still ends the line, or nothing ever would — and
        // ESC is exempt because ESC[#z is the only way a server can leave locked mode again, which
        // the NukeFire prompt does between every one of its own tags.
        if (_lineMode == MxpLineMode.Locked && ch != '\n' && ch != '\r' && ch != '\x1b')
        {
            _run.Append(ch);
            return;
        }

        switch (ch)
        {
            case '<':
                _tag.Clear();
                _mode = Mode.Tag;
                break;

            case '&':
                _entity.Clear();
                _mode = Mode.Entity;
                break;

            case '\n':
                CompleteLine();
                break;

            case '\r':
                // Carriage returns are dropped; scrollback is line-based.
                break;

            case '\x1b':
                _seq.Clear();
                _mode = Mode.Escape;
                break;

            default:
                // Anything else is literal text.
                _run.Append(ch);
                break;
        }
    }

    private void ProcessEscape(char ch)
    {
        switch (ch)
        {
            case '[':
                _seq.Clear();
                _mode = Mode.Csi;
                break;

            case ']':
                _seq.Clear();
                _mode = Mode.Osc;
                break;

            case '(':
            case ')':
            case '*':
            case '+':
            case '#':
            case '%':
                _mode = Mode.EscapeIntermediate;
                break;

            case 'P': // DCS
            case 'X': // SOS
            case '^': // PM
            case '_': // APC
                // String sequences terminated by ST (ESC \) — consume like an OSC so their
                // payloads never leak into the output as text.
                _seq.Clear();
                _mode = Mode.Osc;
                break;

            default:
                // Two-byte escape (ESC c, ESC M, ...) — consumed and ignored. It is not a line tag,
                // so like every other non-ESC[#z sequence it disarms a pending TEMP SECURE.
                EndSequence();
                break;
        }
    }

    private void ProcessCsi(char ch)
    {
        if (ch is >= '\x40' and <= '\x7e')
        {
            if (ch == 'z')
            {
                // The MXP line tag — the one CSI sequence that changes what this parser trusts, and
                // the only one that may leave a pending TEMP SECURE armed.
                ApplyLineTag(_seq.ToString());
                _seq.Clear();
                _mode = Mode.Text;
                return;
            }

            if (ch == 'm')
            {
                // Text accumulated so far keeps the pre-change style.
                FlushRun();
                _current = SgrCodes.Apply(_current, _seq.ToString());
            }

            // Every other final byte — cursor movement, erase — is discarded, which is what a
            // line-oriented view can do with it.
            EndSequence();
            return;
        }

        if (ch is >= '\x20' and <= '\x3f')
        {
            if (_seq.Length < MaxSequenceLength)
            {
                _seq.Append(ch);
            }

            return;
        }

        // A control character inside the sequence aborts it as malformed.
        EndSequence();
    }

    /// <summary>
    /// Returns to text from an escape sequence that was <em>not</em> an <c>ESC[#z</c> line tag.
    /// TEMP SECURE requires the very next thing to be a <c>&lt;</c>, so anything else the server put
    /// in between disarms it — see <see cref="ProcessText"/>.
    /// </summary>
    private void EndSequence()
    {
        _tempSecure = false;
        _seq.Clear();
        _mode = Mode.Text;
    }

    /// <summary>
    /// Applies an <c>ESC[#z</c> line tag. An unparseable or unknown number is ignored rather than
    /// guessed at: a mode this client invents is a mode the server did not ask for, and the
    /// consequences run in the direction of trusting text that was not meant to be trusted.
    /// </summary>
    private void ApplyLineTag(string parameters)
    {
        // NumberStyles.None: digits and nothing else. Integer would accept a sign and surrounding
        // whitespace, and a spelling the spec does not have is a spelling nothing should act on.
        var known = int.TryParse(parameters, NumberStyles.None, CultureInfo.InvariantCulture, out var tag);

        // Only ESC[4z itself leaves a pending TEMP SECURE armed. Every other line tag — and every
        // ESC[#z this client cannot read — is "something other than a '<'", so it disarms, which is
        // the same rule <see cref="EndSequence"/> applies to the sequences that do not come through
        // here. Defence in depth rather than a reachable hole: only a server can emit a line tag, and
        // one that can emit ESC[0z can emit ESC[1z and skip the mechanism entirely.
        if (!known || tag != 4)
        {
            _tempSecure = false;
        }

        if (!known)
        {
            return;
        }

        switch (tag)
        {
            case 0: SetLineMode(MxpLineMode.Open); break;
            case 1: SetLineMode(MxpLineMode.Secure); break;
            case 2: SetLineMode(MxpLineMode.Locked); break;
            case 3: ApplyReset(); break;
            case 4: _tempSecure = true; break;
            case 5: SetLineMode(MxpLineMode.Open); _defaultMode = MxpLineMode.Open; break;
            case 6: SetLineMode(MxpLineMode.Secure); _defaultMode = MxpLineMode.Secure; break;
            case 7: SetLineMode(MxpLineMode.Locked); _defaultMode = MxpLineMode.Locked; break;
        }
    }

    /// <summary>
    /// Moves the current line's mode, honouring the spec's first auto-close rule: "when the mode is
    /// changed from OPEN mode to any other mode, any unclosed OPEN tags (tags that were used while in
    /// open mode) are automatically closed."
    /// </summary>
    /// <remarks>
    /// This is the spec's own bound on how far player-authored markup reaches, and it is why the spec
    /// is willing to call <c>COLOR</c> an open tag at all: without it a
    /// <c>&lt;COLOR FORE=black BACK=black&gt;</c> typed into a public channel paints the rest of the
    /// session black on black, and the tag stack grows without bound. TEMP SECURE is deliberately not
    /// a mode change — it arms the next tag and leaves <see cref="_lineMode"/> alone.
    /// </remarks>
    private void SetLineMode(MxpLineMode mode)
    {
        if (_lineMode == MxpLineMode.Open && mode != MxpLineMode.Open)
        {
            CloseOpenModeTags();
        }

        _lineMode = mode;
    }

    /// <summary>
    /// Closes every still-open tag that was opened while the line was in OPEN mode.
    /// </summary>
    /// <remarks>
    /// Done by closing the <em>outermost</em> such frame: everything nested inside it goes with it,
    /// which is the same rule <see cref="CloseTag"/> applies to an explicit closer, and it is the only
    /// rule that can restore a coherent style — a secure tag opened <em>inside</em> a player's open one
    /// cannot outlive it, however the spec words the exemption.
    /// </remarks>
    private void CloseOpenModeTags()
    {
        for (var i = 0; i < _stack.Count; i++)
        {
            if (_stack[i].OpenedInOpenMode)
            {
                FlushRun();
                CloseFramesFrom(i);
                return;
            }
        }
    }

    /// <summary>Spec: "close all open tags. Set mode to Open. Set text color and properties to default."</summary>
    private void ApplyReset()
    {
        FlushRun();
        CloseInteractionsAtBoundary();
        _stack.Clear();
        _current = TextStyle.Default;
        _interaction = null;
        _tempSecure = false;
        _defaultMode = _lineMode = MxpLineMode.Open;
    }

    /// <summary>
    /// Whether a tag may act on this line, per the mode — and, if a TEMP SECURE arming is what allows
    /// it, <b>spends that arming</b>. Named for the side effect rather than for the answer, because a
    /// reader who takes this for a pure predicate will eventually add a second call.
    /// </summary>
    /// <remarks>
    /// The whole TEMP SECURE rule is "the next tag, and only the next tag", so its correctness is
    /// exactly the property that this runs once per tag. It does today — <c>ProcessTag</c> calls it
    /// once on the closing path and once on the opening one, and those are the only call sites. A
    /// pre-check, a log line or an assertion added later would silently spend the arming and turn a
    /// server-secured tag into a refused one, and there is no test that could name that mistake
    /// because the tag simply renders as text.
    /// </remarks>
    /// <param name="name">
    /// The canonical element name with any leading slash already stripped. Canonical is safe to gate
    /// on because <see cref="Canonical"/> only ever folds an open tag's alternative spellings onto
    /// another open tag (BOLD → B, C → COLOR); no secure tag can canonicalise into the allow-list.
    /// </param>
    private bool ConsumeAuthorizationFor(string name)
    {
        if (_lineMode == MxpLineMode.Secure || _tempSecure)
        {
            // TEMP SECURE is spent by the next tag whatever that tag turns out to be.
            _tempSecure = false;
            return true;
        }

        return MxpTagCategory.IsOpen(name);
    }

    private void ProcessOsc(char ch)
    {
        switch (ch)
        {
            case '\x07': // BEL terminator
                EndSequence();
                break;

            case '\x1b': // possible ST (ESC \)
                _mode = Mode.OscEscape;
                break;

            default:
                if (_seq.Length < MaxSequenceLength)
                {
                    _seq.Append(ch);
                }

                break;
        }
    }

    private void ProcessTagChar(char ch)
    {
        if (_tag.Length == 0)
        {
            // Decide whether the '<' actually begins a tag.
            if (ch == '>')
            {
                // Empty "<>" — not a tag; emit literally.
                _run.Append('<').Append('>');
                _mode = Mode.Text;
                return;
            }

            if (!(char.IsLetter(ch) || ch == '/' || ch == '!'))
            {
                // Stray '<' (e.g. "a < b") — emit it literally and reprocess this char.
                _run.Append('<');
                _mode = Mode.Text;
                ProcessText(ch);
                return;
            }

            _tag.Append(ch);
            return;
        }

        switch (ch)
        {
            case '>':
                ProcessTag(_tag.ToString());
                _tag.Clear();
                _mode = Mode.Text;
                break;

            case '\n':
                // A newline inside a "tag" means it was never a tag; bail out literally.
                _run.Append('<').Append(_tag);
                _tag.Clear();
                _mode = Mode.Text;
                ProcessText('\n');
                break;

            default:
                if (_tag.Length >= MaxTagLength)
                {
                    _run.Append('<').Append(_tag);
                    _tag.Clear();
                    _mode = Mode.Text;
                    ProcessText(ch);
                    return;
                }

                _tag.Append(ch);
                break;
        }
    }

    private void ProcessEntityChar(char ch)
    {
        if (ch == ';')
        {
            var content = _entity.ToString();
            _entity.Clear();
            _mode = Mode.Text;

            var replacement = ResolveEntity(content);
            if (replacement is not null)
            {
                _run.Append(replacement);
            }
            else
            {
                // Unknown entity — emit the raw text so nothing is lost.
                _run.Append('&').Append(content).Append(';');
            }

            return;
        }

        if ((char.IsLetterOrDigit(ch) || ch == '#') && _entity.Length < MaxEntityLength)
        {
            _entity.Append(ch);
            return;
        }

        // Not a valid entity (stray '&' or malformed) — emit literally and reprocess.
        _run.Append('&').Append(_entity);
        _entity.Clear();
        _mode = Mode.Text;
        ProcessText(ch);
    }

    private static string? ResolveEntity(string content)
    {
        if (content.Length == 0)
        {
            return null;
        }

        if (content[0] == '#')
        {
            var digits = content.AsSpan(1);
            int code;
            if (digits.Length > 1 && (digits[0] == 'x' || digits[0] == 'X'))
            {
                if (!int.TryParse(digits[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                {
                    return null;
                }
            }
            else if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
            {
                return null;
            }

            if (code < 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
            {
                return null;
            }

            return char.ConvertFromUtf32(code);
        }

        return content.ToLowerInvariant() switch
        {
            "lt" => "<",
            "gt" => ">",
            "amp" => "&",
            "quot" => "\"",
            "apos" => "'",
            "nbsp" => " ",
            _ => null,
        };
    }

    /// <param name="raw">
    /// The exact tag body as it arrived, everything between the <c>&lt;</c> and the <c>&gt;</c>.
    /// It is kept unmodified all the way to <see cref="EmitLiteralTag"/> so a refused tag is echoed
    /// byte for byte: re-serialising it from the parsed name and attributes would normalise case,
    /// spacing and quoting, and every character the round trip normalises is one a player could use
    /// to smuggle something past a reader being shown "what the other player typed".
    /// </param>
    private void ProcessTag(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (trimmed[0] == '/')
        {
            // A closing tag takes the category of the element it closes, so the slash comes off
            // before the gate sees the name. The gate is consulted unconditionally because a close is
            // still "the next tag" and must spend a pending TEMP SECURE either way.
            var closing = Canonical(trimmed[1..].Trim());
            var allowed = ConsumeAuthorizationFor(closing);

            // A close that matches something open is honoured whatever the mode, because closing can
            // only ever *reduce* privilege. Refusing it leaves the frame open, and a deferred
            // <send> — one with no HREF, whose command is its enclosed text — then absorbs every span
            // to the end of the line, so "\x1b[4z<send>Y</send> Rivane says, 'hi'" finalised as the
            // command "Y</send> Rivane says, 'hi'" with the player supplying the tail. The worst a
            // player achieves under this rule is truncating a clickable region the server drew.
            if (allowed || FindOpenFrame(closing) >= 0)
            {
                CloseTag(closing);
                return;
            }

            EmitLiteralTag(raw);
            return;
        }

        // Tolerate a self-closing slash, e.g. "<BR/>".
        if (trimmed[^1] == '/')
        {
            trimmed = trimmed[..^1].TrimEnd();
            if (trimmed.Length == 0)
            {
                return;
            }
        }

        // Split "NAME rest-of-attributes".
        var nameEnd = 0;
        while (nameEnd < trimmed.Length && !char.IsWhiteSpace(trimmed[nameEnd]))
        {
            nameEnd++;
        }

        var name = Canonical(trimmed[..nameEnd]);
        if (!ConsumeAuthorizationFor(name))
        {
            EmitLiteralTag(raw);
            return;
        }

        var attrs = nameEnd < trimmed.Length ? trimmed[(nameEnd + 1)..] : string.Empty;
        HandleOpener(name, attrs);
    }

    /// <summary>
    /// Writes a tag the mode refused out as the literal text it arrived as, angle brackets and all,
    /// so an injection attempt is shown to the player rather than silently swallowed.
    /// </summary>
    private void EmitLiteralTag(string raw) => _run.Append('<').Append(raw).Append('>');

    private void HandleOpener(string name, string attrs)
    {
        switch (name)
        {
            case "B":
                OpenFormatting(name, TextAttributes.Bold);
                break;
            case "I":
                OpenFormatting(name, TextAttributes.Italic);
                break;
            case "U":
                OpenFormatting(name, TextAttributes.Underline);
                break;
            case "S":
                OpenFormatting(name, TextAttributes.Strikethrough);
                break;
            case "COLOR":
            case "FONT":
                OpenColor(name, attrs);
                break;
            case "SEND":
                OpenSend(attrs);
                break;
            case "A":
                OpenLink(attrs);
                break;
            case "BR":
                CompleteLine();
                break;
            case "VERSION":
                HandleVersionRequest();
                break;
            case "SUPPORT":
                HandleSupportRequest();
                break;
            default:
                // Unknown/unsupported tag (VAR, EXPIRE, IMG, H1, P, …) — consumed and ignored.
                break;
        }
    }

    /// <summary>
    /// Spec's exact attribute order: <c>MXP=mxpversion STYLE=styleversion CLIENT=clientname
    /// VERSION=clientversion REGISTERED=yes/no</c>, with only <c>REGISTERED</c> optional.
    /// </summary>
    /// <remarks>
    /// Values chosen deliberately, not copied from any example:
    /// <list type="bullet">
    /// <item><c>MXP=1.0</c> is the protocol revision this parser's own mechanism — the OPEN/SECURE/LOCKED
    /// line-security model, RESET, TEMP SECURE — implements against the spec dated "Version 1.0
    /// (12-Mar-03)", which is the whole spec's own ceiling; this is not a claim about tag coverage,
    /// which <see cref="SupportedTags"/> answers separately and honestly via SUPPORTS.</item>
    /// <item><c>STYLE=0</c>: "the current version of the optional style sheet[,] set by [the MUD]
    /// sending the <c>&lt;VERSION styleversion&gt;</c> tag to the client." This parser tracks no such
    /// state — no style sheet is ever in effect — so 0 says exactly that rather than inventing a
    /// version number for a mechanism that does not exist here.</item>
    /// <item><c>VERSION=1</c>: this client carries no build/assembly version reachable from this layer
    /// without assembly reflection, which does not belong in a line parser. Kept as a fixed placeholder
    /// rather than manufactured.</item>
    /// <item><c>REGISTERED</c> omitted: it is the one optional field and there is no registration
    /// concept for this client to report.</item>
    /// </list>
    /// </remarks>
    private void HandleVersionRequest()
    {
        ClientReply?.Invoke(this, $"{SecureLinePrefix}<VERSION MXP=1.0 STYLE=0 CLIENT={ClientName} VERSION=1>");
    }

    private void HandleSupportRequest()
    {
        ClientReply?.Invoke(this, $"{SecureLinePrefix}<SUPPORTS {string.Join(' ', SupportedTags)}>");
    }

    private void OpenFormatting(string name, TextAttributes attribute)
    {
        PushFrame(name, isInteraction: false, isLink: false, deferCommand: false, hint: null, promptOnly: false);
        _current = _current.AddAttribute(attribute);
    }

    private void OpenColor(string name, string attrs)
    {
        var parsed = ParseAttributes(attrs);
        // FONT carries its foreground on COLOR=; COLOR/C use FORE=. Positional first = fore.
        var fore = GetAttr(parsed, "FORE") ?? GetAttr(parsed, "COLOR") ?? Positional(parsed, 0);
        var back = GetAttr(parsed, "BACK") ?? Positional(parsed, 1);

        PushFrame(name, isInteraction: false, isLink: false, deferCommand: false, hint: null, promptOnly: false);

        if (fore is not null && WebColors.TryParse(fore, out var fg))
        {
            _current = _current.WithForeground(fg);
        }

        if (back is not null && WebColors.TryParse(back, out var bg))
        {
            _current = _current.WithBackground(bg);
        }
    }

    private void OpenSend(string attrs)
    {
        var parsed = ParseAttributes(attrs);
        var href = GetAttr(parsed, "HREF") ?? Positional(parsed, 0);
        var hint = GetAttr(parsed, "HINT") ?? Positional(parsed, 1);
        var prompt = HasFlag(parsed, "PROMPT");

        if (href is not null)
        {
            // HREF may hold several '|'-separated commands; the first is the primary command.
            var primary = href.Split('|', 2)[0];
            PushFrame("SEND", isInteraction: true, isLink: false, deferCommand: false, hint: hint, promptOnly: prompt);
            _interaction = SpanInteraction.Command(primary, hint, prompt);
        }
        else
        {
            // No HREF: the enclosed text is itself the command (resolved when the tag closes).
            PushFrame("SEND", isInteraction: true, isLink: false, deferCommand: true, hint: hint, promptOnly: prompt);
            _interaction = SpanInteraction.Command(string.Empty, hint, prompt);
        }
    }

    private void OpenLink(string attrs)
    {
        var parsed = ParseAttributes(attrs);
        var href = GetAttr(parsed, "HREF") ?? Positional(parsed, 0);
        var hint = GetAttr(parsed, "HINT") ?? Positional(parsed, 1);

        if (href is not null)
        {
            PushFrame("A", isInteraction: true, isLink: true, deferCommand: false, hint: hint, promptOnly: false);
            _interaction = SpanInteraction.Link(href, hint);
        }
        else
        {
            PushFrame("A", isInteraction: true, isLink: true, deferCommand: true, hint: hint, promptOnly: false);
            _interaction = SpanInteraction.Link(string.Empty, hint);
        }
    }

    private void PushFrame(string name, bool isInteraction, bool isLink, bool deferCommand, string? hint, bool promptOnly)
    {
        FlushRun();
        _stack.Add(new Frame
        {
            Name = name,
            SavedStyle = _current,
            SavedInteraction = _interaction,
            IsInteraction = isInteraction,
            IsLink = isLink,
            DeferCommand = deferCommand,
            Hint = hint,
            PromptOnly = promptOnly,
            SpanStart = _lineSpans.Count,
            OpenedInOpenMode = _lineMode == MxpLineMode.Open,
        });
    }

    /// <summary>
    /// The index of the innermost open frame for <paramref name="name"/>, or <c>-1</c> when the
    /// element is not open.
    /// </summary>
    private int FindOpenFrame(string name)
    {
        for (var i = _stack.Count - 1; i >= 0; i--)
        {
            if (_stack[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }

    private void CloseTag(string name)
    {
        var idx = FindOpenFrame(name);
        if (idx < 0)
        {
            // Stray/unbalanced closer — ignored (does not throw).
            return;
        }

        FlushRun();
        CloseFramesFrom(idx);
    }

    /// <summary>
    /// Pops every frame at or above <paramref name="idx"/>, finalising any deferred interaction among
    /// them, and restores the style and interaction that were in force before frame
    /// <paramref name="idx"/> opened. Callers flush the pending run first, so the text written under
    /// those frames keeps their rendition.
    /// </summary>
    private void CloseFramesFrom(int idx)
    {
        var matched = _stack[idx];
        for (var i = _stack.Count - 1; i >= idx; i--)
        {
            var frame = _stack[i];
            if (frame.IsInteraction && frame.DeferCommand)
            {
                FinalizeDeferredInteraction(frame);
            }

            _stack.RemoveAt(i);
        }

        // Restoring the matched frame's saved state reverts everything opened at or after it.
        _current = matched.SavedStyle;
        _interaction = matched.SavedInteraction;
    }

    /// <summary>
    /// Rewrites the spans emitted since a deferred <c>&lt;SEND&gt;</c>/<c>&lt;A&gt;</c> opened,
    /// using their concatenated text as the command/URL target.
    /// </summary>
    private void FinalizeDeferredInteraction(Frame frame)
    {
        if (frame.SpanStart >= _lineSpans.Count)
        {
            return;
        }

        var sb = new StringBuilder();
        for (var i = frame.SpanStart; i < _lineSpans.Count; i++)
        {
            sb.Append(_lineSpans[i].Text);
        }

        var target = sb.ToString();
        var interaction = frame.IsLink
            ? SpanInteraction.Link(target, frame.Hint)
            : SpanInteraction.Command(target, frame.Hint, frame.PromptOnly);

        for (var i = frame.SpanStart; i < _lineSpans.Count; i++)
        {
            var span = _lineSpans[i];
            _lineSpans[i] = new StyledSpan(span.Text, span.Style, interaction);
        }
    }

    /// <summary>
    /// Finalises and removes any open interaction (<c>&lt;SEND&gt;</c>/<c>&lt;A&gt;</c>) frames at a
    /// line/prompt boundary so a bare, unclosed interaction never leaks to the next line.
    /// Formatting/colour frames persist across the boundary (MXP tags may span lines).
    /// </summary>
    private void CloseInteractionsAtBoundary()
    {
        for (var i = _stack.Count - 1; i >= 0; i--)
        {
            var frame = _stack[i];
            if (!frame.IsInteraction)
            {
                continue;
            }

            if (frame.DeferCommand)
            {
                FinalizeDeferredInteraction(frame);
            }

            _stack.RemoveAt(i);
        }

        // A formatting frame opened *inside* an interaction still holds it in SavedInteraction;
        // if it closes on a later line, CloseTag would resurrect the ended interaction. Clear it
        // (and rebase SpanStart, which pointed into the now-cleared line) on surviving frames.
        for (var i = 0; i < _stack.Count; i++)
        {
            var frame = _stack[i];
            if (frame.SavedInteraction is not null || frame.SpanStart != 0)
            {
                _stack[i] = new Frame
                {
                    Name = frame.Name,
                    SavedStyle = frame.SavedStyle,
                    SavedInteraction = null,
                    IsInteraction = frame.IsInteraction,
                    IsLink = frame.IsLink,
                    DeferCommand = frame.DeferCommand,
                    Hint = frame.Hint,
                    PromptOnly = frame.PromptOnly,
                    SpanStart = 0,
                };
            }
        }

        _interaction = null;
    }

    private void CompleteLine()
    {
        EndLine();

        var line = _lineSpans.Count == 0 ? StyledLine.Empty : new StyledLine(_lineSpans);
        _lineSpans.Clear();
        (_emit ??= new List<StyledLine>()).Add(line);
    }

    /// <summary>
    /// Everything the end of a line does to parser state, short of emitting the line itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called from <see cref="CompleteLine"/> <em>and</em> from <see cref="Flush"/>, because
    /// <see cref="Flush"/> is the line boundary this parser actually sees in production: the telnet
    /// layer strips the terminator, so <c>WorldSession.OnOutputReceived</c> feeds a line's text with
    /// no <c>'\n'</c> in it and then flushes. A revert that lived only in <see cref="CompleteLine"/>
    /// therefore never ran on a real connection — one <c>ESC[1z</c> from the server and every
    /// following line of the session stayed SECURE, so a <c>&lt;SEND&gt;</c> a player typed into a
    /// public channel became a clickable command. <see cref="Flush"/> was already the boundary for
    /// the tag stack (<see cref="CloseInteractionsAtBoundary"/>); this makes it the boundary for
    /// everything, which is the property that cannot be forgotten by a future caller — a consumer
    /// that does not flush gets no output at all, whereas one that forgot to call an
    /// <c>EndLine()</c> on <c>ILineParser</c> would silently reproduce exactly this bug.
    /// </para>
    /// </remarks>
    private void EndLine()
    {
        FlushRun();

        // Spec: "when in OPEN mode, any unclosed OPEN tags are automatically closed when a newline is
        // received from the MUD." Only open-mode ones: "secure tags are never automatically closed",
        // which is why a server's markup may span lines and a player's may not.
        if (_lineMode == MxpLineMode.Open)
        {
            CloseOpenModeTags();
        }

        CloseInteractionsAtBoundary();

        // An escape sequence cannot span the boundary either — see Process. In production this is the
        // only place it can be aborted, since the terminator the '\n' rule keys on never arrives.
        if (IsEscapeMode(_mode))
        {
            EndSequence();
        }

        // Spec: "when a newline is received from the MUD, the mode reverts back to the Default mode."
        // A pending TEMP SECURE dies with the line rather than arming the first tag of the next one.
        _lineMode = _defaultMode;
        _tempSecure = false;
    }

    private void FlushRun()
    {
        if (_run.Length == 0)
        {
            return;
        }

        _lineSpans.Add(new StyledSpan(_run.ToString(), _current, _interaction));
        _run.Clear();
    }

    private static string Canonical(string name) => name.ToUpperInvariant() switch
    {
        "B" or "BOLD" or "STRONG" => "B",
        "I" or "ITALIC" or "EM" => "I",
        "U" or "UNDERLINE" => "U",
        "S" or "STRIKEOUT" => "S",
        "C" or "COLOR" => "COLOR",
        var other => other,
    };

    // ----- Attribute parsing --------------------------------------------------

    /// <summary>
    /// Tokenises an attribute string into <c>(Key, Value)</c> pairs. A <c>KEY=VALUE</c> pair has a
    /// non-null key; a bare token (positional value or flag) has a null key. Values may be quoted
    /// with <c>"</c> or <c>'</c>, or be a bare whitespace-delimited token.
    /// </summary>
    private static List<(string? Key, string Value)> ParseAttributes(string s)
    {
        var result = new List<(string?, string)>();
        var i = 0;
        var n = s.Length;

        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(s[i]))
            {
                i++;
            }

            if (i >= n)
            {
                break;
            }

            var quoted = s[i] == '"' || s[i] == '\'';
            var token = ReadToken(s, ref i, stopAtEquals: !quoted);

            if (!quoted && i < n && s[i] == '=')
            {
                i++; // consume '='
                var value = ReadToken(s, ref i, stopAtEquals: false);
                result.Add((token, value));
            }
            else
            {
                result.Add((null, token));
            }
        }

        return result;
    }

    private static string ReadToken(string s, ref int i, bool stopAtEquals)
    {
        var n = s.Length;
        if (i < n && (s[i] == '"' || s[i] == '\''))
        {
            var quote = s[i++];
            var start = i;
            while (i < n && s[i] != quote)
            {
                i++;
            }

            var value = s[start..i];
            if (i < n)
            {
                i++; // consume closing quote
            }

            return value;
        }

        var tokenStart = i;
        while (i < n && !char.IsWhiteSpace(s[i]) && !(stopAtEquals && s[i] == '='))
        {
            i++;
        }

        return s[tokenStart..i];
    }

    private static string? GetAttr(List<(string? Key, string Value)> attrs, string key)
    {
        foreach (var (k, v) in attrs)
        {
            if (k is not null && string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                return v;
            }
        }

        return null;
    }

    private static bool HasFlag(List<(string? Key, string Value)> attrs, string flag)
    {
        foreach (var (k, v) in attrs)
        {
            if (k is not null && string.Equals(k, flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (k is null && string.Equals(v, flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the <paramref name="index"/>-th positional (unkeyed) value, skipping the PROMPT flag.</summary>
    private static string? Positional(List<(string? Key, string Value)> attrs, int index)
    {
        var seen = 0;
        foreach (var (k, v) in attrs)
        {
            if (k is not null || string.Equals(v, "PROMPT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seen == index)
            {
                return v;
            }

            seen++;
        }

        return null;
    }
}
