using System.Globalization;
using System.Text;

namespace SharpMUTerm.Core.Telnet;

/// <summary>
/// Observes CSI 16 t before TNC buffers a line. The existing ANSI parser still consumes the escape;
/// all input is passed through unchanged. Never answers queries embedded in OSC/DCS/APC strings,
/// and never forwards server escapes to the local terminal. Only measured cell pixels are disclosed.
/// </summary>
internal sealed class TerminalCellQueryResponder(Func<TerminalCellSize?> measure, Func<ReadOnlyMemory<byte>, ValueTask> send)
{
    private enum State { Ground, Escape, Csi, String, StringEscape }
    private State _state;
    private bool _osc;
    private int _queryLength;
    private bool _candidate;
    private int _codePage = -1;
    private bool _asciiCompatible;
    private volatile bool _asked;
    private volatile bool _stopped;

    internal void Stop() => _stopped = true;
    private TerminalCellSize? _sent;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    // The rendering parsers abandon unfinished escapes on a line boundary too.
    internal void FinishLine() => _state = State.Ground;

    internal async ValueTask ObserveAsync(byte value, Encoding encoding)
    {
        // Check the control syntax rather than guessing from single-/multibyte classification:
        // Shift-JIS is ASCII-compatible; UTF-16 and EBCDIC are not.
        if (_codePage != encoding.CodePage)
        {
            _codePage = encoding.CodePage;
            _asciiCompatible = encoding.GetBytes("\u001b[16t]P_^X\\\a").AsSpan().SequenceEqual("\u001b[16t]P_^X\\\a"u8);
        }

        if (_stopped || !_asciiCompatible)
        {
            _state = State.Ground;
            return;
        }

        if (value is 0x18 or 0x1a or 10)
        {
            _state = State.Ground;
            return;
        }

        if (_state is State.String or State.StringEscape)
        {
            if ((_osc && value == 7) || (_state == State.StringEscape && value == '\\'))
            {
                _state = State.Ground;
            }
            else
            {
                _state = value == 27 ? State.StringEscape : State.String;
            }

            return;
        }

        if (value == 27)
        {
            _state = State.Escape;
            return;
        }

        switch (_state)
        {
            case State.Escape:
                _state = value switch
                {
                    (byte)'[' => State.Csi,
                    (byte)']' or (byte)'P' or (byte)'_' or (byte)'^' or (byte)'X' => State.String,
                    _ => State.Ground,
                };
                _osc = value == ']';
                _queryLength = 0;
                _candidate = true;
                break;
            case State.Csi:
                if (value is >= 0x40 and <= 0x7e)
                {
                    _state = State.Ground;
                    if (_candidate && _queryLength == 2 && value == 't')
                    {
                        _asked = true;
                        await ReportAsync(force: true).ConfigureAwait(false);
                    }
                }
                else if (value is >= 0x20 and <= 0x3f)
                {
                    _candidate &= _queryLength switch { 0 => value == '1', 1 => value == '6', _ => false };
                    _queryLength = Math.Min(3, _queryLength + 1);
                }
                else
                {
                    _state = State.Ground;
                }

                break;
        }

        return;
    }

    /// <summary>Refresh only a server that has asked, and only when the measured pixels changed.</summary>
    internal async ValueTask ReportAsync(bool force = false)
    {
        if (_stopped || !_asked)
        {
            return;
        }

        await _sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Re-read after waiting so a zoom burst cannot publish an older size after a newer one.
            if (!_stopped && measure() is { IsValid: true } current && (force || current != _sent))
            {
                var reply = string.Create(CultureInfo.InvariantCulture, $"\u001b[6;{current.Height};{current.Width}t");
                await send(Encoding.ASCII.GetBytes(reply)).ConfigureAwait(false);
                Volatile.Write(ref _sent, current);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }
}
