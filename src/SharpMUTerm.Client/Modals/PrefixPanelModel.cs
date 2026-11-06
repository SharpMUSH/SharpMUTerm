using SharpMUTerm.Client.Chrome;

namespace SharpMUTerm.Client.Modals;

/// <summary>
/// The ⌃B which-key panel in a host's own controls: the keymap the cell panel draws (<see cref="HeaderPrefix"/>, the
/// same instance the header strip reads), and the one gesture it has, a key. Any key ends the panel and goes back to the
/// client's prefix consumer, which is where a key pressed into the cell panel, or before it appeared, goes too: the
/// panel decides nothing about what a key means. Esc, a press beside the card and ⌃B again all cancel.
/// </summary>
public sealed class PrefixPanelSession : ModalSession<HeaderPrefix>
{
    private readonly Action<ConsoleKeyInfo> _key;

    /// <param name="model">The keymap, availability and all, as the cells draw it.</param>
    /// <param name="panes">How many panes there are: the digits the <c>1–9</c> row can go to.</param>
    /// <param name="isDark">Whether the client's theme is dark.</param>
    /// <param name="key">The client's prefix consumer, handed whichever key ends the panel.</param>
    /// <param name="closed">Raised once the panel has gone, however it went.</param>
    public PrefixPanelSession(HeaderPrefix model, int panes, bool isDark, Action<ConsoleKeyInfo> key, Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(key);
        _key = key;
        Panes = panes;
    }

    public override string Surface => Surfaces.SurfaceCatalog.PrefixPanel;

    /// <summary>How many panes the workspace has, so a click can go to each by number.</summary>
    public int Panes { get; }

    /// <summary>The panel's title, as both hosts word it.</summary>
    public const string Title = "⌃B — pane commands";

    /// <summary>A key pressed into the panel: it ends the panel and is run as the prefix's key.</summary>
    public void Key(ConsoleKeyInfo key) => CloseThen(() => _key(key));

    /// <summary>A row's key clicked: the same as pressing it.</summary>
    public void Press(char key) => Key(new ConsoleKeyInfo(key, ConsoleKey.NoName, false, false, false));

    /// <summary>
    /// The keys a row's <see cref="HeaderPrefixKey.Keys"/> stands for, each pressable: <c>| </c> is one, <c>&lt; &gt;
    /// ← →</c> is the two the arrows repeat, and <c>1–9</c> is one per pane that exists.
    /// </summary>
    public IReadOnlyList<char> KeysOf(HeaderPrefixKey row)
    {
        if (row.Keys.Contains('–'))
        {
            return [.. Enumerable.Range(1, Math.Clamp(Panes, 0, 9)).Select(n => (char)('0' + n))];
        }

        return [.. row.Keys.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(k => k.Length == 1 && k[0] is not ('←' or '→')).Select(k => k[0])];
    }
}
