using System.Text;

namespace SharpMUTerm.Core.Text;

/// <summary>
/// Incremental, line-oriented ANSI/VT parser. Feed it decoded text (any number of
/// chunks); it emits fully-terminated <see cref="StyledLine"/>s and retains the
/// in-progress line plus the current <see cref="TextStyle"/> across calls, so escape
/// sequences and colour state may span chunk boundaries.
///
/// SGR handling covers the 16 base colours, the 256-colour palette (<c>38;5;n</c>),
/// 24-bit truecolour (<c>38;2;r;g;b</c>, including the colon-delimited ISO form), and
/// the common rendition attributes. Non-SGR CSI sequences (cursor movement, erase),
/// OSC strings, and other escapes are recognised and discarded rather than leaking
/// into the output as stray text.
/// </summary>
public sealed class AnsiParser : ILineParser
{
    private const int MaxSequenceLength = 128;

    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        Csi,
        Osc,
        OscEscape,
    }

    private State _state = State.Ground;
    private TextStyle _current = TextStyle.Default;
    private readonly StringBuilder _run = new();
    private readonly List<StyledSpan> _lineSpans = new();
    private readonly StringBuilder _seq = new();

    /// <summary>The rendition state that will apply to the next printed character.</summary>
    public TextStyle CurrentStyle => _current;

    /// <summary>True when a partial line or escape sequence is buffered.</summary>
    public bool HasPendingContent => _run.Length > 0 || _lineSpans.Count > 0 || _state != State.Ground;

    /// <summary>Feeds a chunk of text, returning every line completed by a newline within it.</summary>
    public IReadOnlyList<StyledLine> Feed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Feed(text.AsSpan());
    }

    /// <summary>Feeds a chunk of text, returning every line completed by a newline within it.</summary>
    public IReadOnlyList<StyledLine> Feed(ReadOnlySpan<char> text)
    {
        List<StyledLine>? lines = null;
        foreach (var ch in text)
        {
            Process(ch, ref lines);
        }

        return (IReadOnlyList<StyledLine>?)lines ?? Array.Empty<StyledLine>();
    }

    /// <summary>
    /// Returns the buffered partial line (e.g. a prompt not terminated by a newline) and
    /// clears it, or <c>null</c> if nothing is buffered. Colour state is preserved.
    /// </summary>
    public StyledLine? Flush()
    {
        FlushRun();
        if (_lineSpans.Count == 0)
        {
            return null;
        }

        var line = new StyledLine(_lineSpans);
        _lineSpans.Clear();
        return line;
    }

    /// <summary>Resets all parser state, including the current style.</summary>
    public void Reset()
    {
        _state = State.Ground;
        _current = TextStyle.Default;
        _run.Clear();
        _lineSpans.Clear();
        _seq.Clear();
    }

    private void Process(char ch, ref List<StyledLine>? lines)
    {
        switch (_state)
        {
            case State.Ground:
                ProcessGround(ch, ref lines);
                break;

            case State.Escape:
                ProcessEscape(ch);
                break;

            case State.EscapeIntermediate:
                // Consume the single trailing byte of e.g. ESC ( B and return to ground.
                _state = State.Ground;
                break;

            case State.Csi:
                ProcessCsi(ch);
                break;

            case State.Osc:
                ProcessOsc(ch);
                break;

            case State.OscEscape:
                // Inside an OSC string we saw ESC; a following '\' is the ST terminator.
                _state = ch == '\\' ? State.Ground : State.Osc;
                break;
        }
    }

    private void ProcessGround(char ch, ref List<StyledLine>? lines)
    {
        switch (ch)
        {
            case '\x1b':
                _state = State.Escape;
                break;

            case '\n':
                CompleteLine(ref lines);
                break;

            case '\r':
                // Carriage returns are dropped; scrollback is line-based.
                break;

            default:
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
                _state = State.Csi;
                break;

            case ']':
                _seq.Clear();
                _state = State.Osc;
                break;

            case '(':
            case ')':
            case '*':
            case '+':
            case '#':
            case '%':
                _state = State.EscapeIntermediate;
                break;

            case 'P': // DCS
            case 'X': // SOS
            case '^': // PM
            case '_': // APC
                // String sequences terminated by ST (ESC \) — consume like an OSC so their
                // payloads never leak into the output as text.
                _seq.Clear();
                _state = State.Osc;
                break;

            default:
                // Two-byte escape (ESC c, ESC M, ...) — consumed and ignored.
                _state = State.Ground;
                break;
        }
    }

    private void ProcessCsi(char ch)
    {
        // Final byte is in the range 0x40-0x7E.
        if (ch is >= '\x40' and <= '\x7e')
        {
            if (ch == 'm')
            {
                ApplySgr(_seq.ToString());
            }

            // All other CSI sequences (cursor, erase, ...) are discarded.
            _seq.Clear();
            _state = State.Ground;
            return;
        }

        // Parameter (0x30-0x3F) and intermediate (0x20-0x2F) bytes accumulate.
        if (ch is >= '\x20' and <= '\x3f')
        {
            if (_seq.Length < MaxSequenceLength)
            {
                _seq.Append(ch);
            }

            return;
        }

        // Anything else (e.g. an embedded control char) aborts the malformed sequence.
        _seq.Clear();
        _state = State.Ground;
    }

    private void ProcessOsc(char ch)
    {
        switch (ch)
        {
            case '\x07': // BEL terminator
                _state = State.Ground;
                break;

            case '\x1b': // possible ST (ESC \)
                _state = State.OscEscape;
                break;

            default:
                if (_seq.Length < MaxSequenceLength)
                {
                    _seq.Append(ch);
                }

                break;
        }
    }

    private void CompleteLine(ref List<StyledLine>? lines)
    {
        FlushRun();
        var line = _lineSpans.Count == 0 ? StyledLine.Empty : new StyledLine(_lineSpans);
        _lineSpans.Clear();
        (lines ??= new List<StyledLine>()).Add(line);
    }

    private void FlushRun()
    {
        if (_run.Length == 0)
        {
            return;
        }

        _lineSpans.Add(new StyledSpan(_run.ToString(), _current));
        _run.Clear();
    }

    private void ApplySgr(string parameters)
    {
        // Text accumulated so far keeps the pre-change style.
        FlushRun();
        _current = SgrCodes.Apply(_current, parameters);
    }
}
