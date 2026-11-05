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
    private IReadOnlyList<PanelOverlay> _panels = [];
    private IReadOnlyList<HeaderAction> _actions = [];
    private ChromeOverlay? _chrome;

    /// <summary>What the pointer is over, as of the client's last word.</summary>
    public PointerTarget Target => Volatile.Read(ref _target);

    /// <summary>The panes' scroll positions, as of the client's last word.</summary>
    public IReadOnlyList<PaneScrollbar> Bars => Volatile.Read(ref _bars);

    /// <summary>The panels on screen the window draws natively, as of the client's last word.</summary>
    public IReadOnlyList<PanelOverlay> Panels => Volatile.Read(ref _panels);

    /// <summary>The header buttons the client offers, said once at start.</summary>
    public IReadOnlyList<HeaderAction> Actions => Volatile.Read(ref _actions);

    /// <summary>The header and the sidebar as the client last described them, or null before it has.</summary>
    public ChromeOverlay? Chrome => Volatile.Read(ref _chrome);

    /// <summary>Raised on the client's thread when <see cref="Chrome"/> changes.</summary>
    public event Action? ChromeChanged;

    /// <summary>Raised on the client's thread when <see cref="Panels"/> changes.</summary>
    public event Action? PanelsChanged;

    /// <summary>Raised on the client's thread when <see cref="Actions"/> changes.</summary>
    public event Action? ActionsChanged;

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

    /// <summary>Raised on the client's thread when a menu the client built as data (the tab overflow list) is asked for.</summary>
    public event Action<System.Drawing.Point, SharpMUTerm.Client.Commands.MenuModel, Action<string>>? ModelMenuRequested;

    /// <summary>The last menu-as-data the client asked for, with its way back. Internal for tests.</summary>
    internal (SharpMUTerm.Client.Commands.MenuModel Menu, Action<string> Dispatch)? LastModelMenu { get; private set; }

    public void ShowMenu(System.Drawing.Point cell, SharpMUTerm.Client.Commands.MenuModel menu, Action<string> dispatch)
    {
        LastModelMenu = (menu, dispatch);
        ModelMenuRequested?.Invoke(cell, menu, dispatch);
    }

    public bool DrawsMenus => true;

    private int _windowColumns;

    /// <summary>
    /// The window's whole width in cells, the native header and sidebar included: written by <see cref="TerminalView"/>
    /// before each resize of the grid, so the client reads it fresh with the resize. Zero until then.
    /// </summary>
    internal int WindowColumnsMeasured
    {
        get => Volatile.Read(ref _windowColumns);
        set => Volatile.Write(ref _windowColumns, value);
    }

    int? IPointerPresenter.WindowColumns => WindowColumnsMeasured is > 0 and var columns ? columns : null;

    private double _cellHeight;

    /// <summary>
    /// A cell's height in the window's logical pixels, written by <see cref="TerminalView"/> when it is made and when its
    /// fonts change; zero while there is no view (a test, the parity harness), when the row rule sizes vitals cards.
    /// </summary>
    internal double CellHeightMeasured
    {
        get => Volatile.Read(ref _cellHeight);
        set => Volatile.Write(ref _cellHeight, value);
    }

    int? IPointerPresenter.VitalsCardRows(int meters, bool target) =>
        CellHeightMeasured is > 0 and var cell ? VitalsGauges.CardRows(meters, target, cell) : null;

    public void Scrollbars(IReadOnlyList<PaneScrollbar> bars)
    {
        Volatile.Write(ref _bars, bars);
        ScrollbarsChanged?.Invoke(bars);
    }

    void IPointerPresenter.Panels(IReadOnlyList<PanelOverlay> panels)
    {
        Volatile.Write(ref _panels, panels);
        PanelsChanged?.Invoke();
    }

    void IPointerPresenter.HeaderActions(IReadOnlyList<HeaderAction> actions)
    {
        Volatile.Write(ref _actions, actions);
        ActionsChanged?.Invoke();
    }

    void IPointerPresenter.Chrome(ChromeOverlay chrome)
    {
        Volatile.Write(ref _chrome, chrome);
        ChromeChanged?.Invoke();
    }

    private PaneChromeOverlay? _paneChrome;

    /// <summary>The panes' tab strips and scrolled-back bands as the client last described them, or null before it has.</summary>
    public PaneChromeOverlay? PaneChrome => Volatile.Read(ref _paneChrome);

    /// <summary>Raised on the client's thread when <see cref="PaneChrome"/> changes.</summary>
    public event Action? PaneChromeChanged;

    void IPointerPresenter.PaneChrome(PaneChromeOverlay chrome)
    {
        Volatile.Write(ref _paneChrome, chrome);
        PaneChromeChanged?.Invoke();
    }

    private IReadOnlyList<NoticeOverlay> _notices = [];

    /// <summary>The notices the client has said, newest last, the most recent <see cref="NoticesKept"/>.</summary>
    public IReadOnlyList<NoticeOverlay> Notices => Volatile.Read(ref _notices);

    /// <summary>How many notices are kept for the toasts; the message log is the history.</summary>
    public const int NoticesKept = 8;

    /// <summary>Raised on the client's thread with each notice the client says.</summary>
    public event Action<NoticeOverlay>? NoticeSaid;

    private StatusOverlay? _status;
    private InputBand? _band;

    /// <summary>The status row as the client last described it, or null before it has (<see cref="HostChrome.Status"/>).</summary>
    public StatusOverlay? Status => Volatile.Read(ref _status);

    /// <summary>
    /// Where the command line's rows are, as of the client's last word (<see cref="HostChrome.Input"/>): read by the
    /// driver when it flushes a frame, so each frame carries the band it was painted with.
    /// </summary>
    public InputBand? Band => Volatile.Read(ref _band);

    /// <summary>Raised on the client's thread when <see cref="Status"/> changes.</summary>
    public event Action? StatusChanged;

    void IPointerPresenter.Status(StatusOverlay status)
    {
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke();
    }

    void IPointerPresenter.InputBand(InputBand? band) => Volatile.Write(ref _band, band);

    private IReadOnlyList<PaneDividerOverlay> _dividers = [];

    /// <summary>
    /// The pane dividers as of the client's last word: read by the driver when it flushes a frame, so each frame carries
    /// the dividers it was painted with (<see cref="ScreenFrame.Dividers"/>).
    /// </summary>
    public IReadOnlyList<PaneDividerOverlay> Dividers => Volatile.Read(ref _dividers);

    void IPointerPresenter.Dividers(IReadOnlyList<PaneDividerOverlay> dividers) => Volatile.Write(ref _dividers, dividers);

    void IPointerPresenter.Notice(NoticeOverlay notice)
    {
        var next = Notices.Append(notice).TakeLast(NoticesKept).ToList();
        Volatile.Write(ref _notices, next);
        NoticeSaid?.Invoke(notice);
    }
}
