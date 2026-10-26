using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's <see cref="IPointerPresenter"/>: it keeps what the client last said and raises an event, and
/// <see cref="TerminalView"/> draws it on the window's thread. The client calls in on its own thread, so this
/// holds no Avalonia state; a test reads it with no window at all.
/// </summary>
internal sealed class WindowPointer : IPointerPresenter
{
    private PointerTarget _target = PointerTarget.Nothing;
    private IReadOnlyList<PaneScrollbar> _bars = [];

    /// <summary>What the pointer is over, as of the client's last word.</summary>
    public PointerTarget Target => Volatile.Read(ref _target);

    /// <summary>The panes' scroll positions, as of the client's last word.</summary>
    public IReadOnlyList<PaneScrollbar> Bars => Volatile.Read(ref _bars);

    /// <summary>Raised on the client's thread when <see cref="Target"/> changes.</summary>
    public event Action<PointerTarget>? HoverChanged;

    /// <summary>Raised on the client's thread when a right-click asks for a menu.</summary>
    public event Action<System.Drawing.Point, IReadOnlyList<PointerMenuItem>>? MenuRequested;

    /// <summary>Raised on the client's thread when <see cref="Bars"/> changes.</summary>
    public event Action<IReadOnlyList<PaneScrollbar>>? ScrollbarsChanged;

    public void Hover(PointerTarget target)
    {
        Volatile.Write(ref _target, target);
        HoverChanged?.Invoke(target);
    }

    public void ShowMenu(System.Drawing.Point cell, IReadOnlyList<PointerMenuItem> items) => MenuRequested?.Invoke(cell, items);

    public bool DrawsMenus => true;

    public void Scrollbars(IReadOnlyList<PaneScrollbar> bars)
    {
        Volatile.Write(ref _bars, bars);
        ScrollbarsChanged?.Invoke(bars);
    }
}
