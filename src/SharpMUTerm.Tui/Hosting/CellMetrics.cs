using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Tui;

/// <summary>A host-owned, thread-safe snapshot of actual physical cell pixels. Unknown is never guessed.</summary>
internal class CellMetrics : IDisposable
{
    private TerminalCellSize? _current;

    public TerminalCellSize? Current
    {
        get => Volatile.Read(ref _current);
        set
        {
            if (Interlocked.Exchange(ref _current, value) != value)
            {
                Changed?.Invoke();
            }
        }
    }

    public event Action? Changed;

    public virtual void Start() { }

    public virtual void Dispose() { }
}
