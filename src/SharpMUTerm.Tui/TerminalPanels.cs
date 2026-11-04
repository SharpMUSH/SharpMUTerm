using SharpConsoleUI.Controls;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Tui;

/// <summary>What a terminal panel is being built for: which panel, whose, and the window that will hold it.</summary>
/// <param name="PanelId">One of <c>PanelIds</c>.</param>
/// <param name="SessionKey">The <c>world.character</c> the panel belongs to.</param>
/// <param name="Session">That character's live session, or null (the snapshot demo has none).</param>
/// <param name="WindowId">The workspace window the control is shown in (<see cref="TerminalPanels.WindowId"/>).</param>
internal sealed record TerminalPanelContext(string PanelId, string SessionKey, WorldSession? Session, string WindowId);

/// <summary>
/// The seam layout panels plug into, keyed by panel id. A panel that is drawn by a pane control of its own — who is
/// here, the vitals strip, the quick bar, the docked composer — registers a factory here at startup
/// (<c>SharpMUTermApp.RegisterTerminalPanels</c>), and the layout code realises it as a workspace window
/// <see cref="WindowId"/> owned by the session, shown as a tab of the pane the layout puts it in.
/// <para>
/// Three panels are not registered here because they are windows already: <c>output</c> (the session's window),
/// <c>chat</c> (its capture windows) and <c>map</c> (<c>map:&lt;key&gt;</c>). An id that is neither those nor
/// registered counts as having no data, so a layout naming it folds it into its <c>Waiting</c> list and draws
/// nothing — an unbuilt panel costs no cells.
/// </para>
/// <para>
/// The factory is called once per window; the control is cached here and re-parented on every pane rebuild, the way
/// the map's control is, so it must survive being moved between containers.
/// </para>
/// </summary>
internal sealed class TerminalPanels
{
    /// <summary>The prefix of every panel window id.</summary>
    public const string WindowPrefix = "panel:";

    private readonly Dictionary<string, Func<TerminalPanelContext, IWindowControl>> _factories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IWindowControl> _controls = new(StringComparer.Ordinal);

    /// <summary>Registers the factory for <paramref name="panelId"/>, replacing any earlier one.</summary>
    public void Register(string panelId, Func<TerminalPanelContext, IWindowControl> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(panelId);
        _factories[panelId] = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>Whether a factory is registered for <paramref name="panelId"/>.</summary>
    public bool IsRegistered(string panelId) => _factories.ContainsKey(panelId);

    /// <summary>The ids with a factory.</summary>
    public IReadOnlyCollection<string> Registered => _factories.Keys;

    /// <summary>The window id that shows <paramref name="panelId"/> for <paramref name="sessionKey"/>.</summary>
    public static string WindowId(string panelId, string sessionKey) => $"{WindowPrefix}{panelId}:{sessionKey}";

    /// <summary>
    /// Reads a panel window id back. The panel id runs to the first colon after the prefix (no panel id has one);
    /// the session key is the rest, colons and all.
    /// </summary>
    public static bool TryParseWindowId(string windowId, out string panelId, out string sessionKey)
    {
        panelId = string.Empty;
        sessionKey = string.Empty;
        if (windowId is null || !windowId.StartsWith(WindowPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = windowId.AsSpan(WindowPrefix.Length);
        var colon = rest.IndexOf(':');
        if (colon <= 0 || colon == rest.Length - 1)
        {
            return false;
        }

        panelId = rest[..colon].ToString();
        sessionKey = rest[(colon + 1)..].ToString();
        return true;
    }

    /// <summary>The control already built for <paramref name="windowId"/>, or null.</summary>
    public IWindowControl? ControlFor(string windowId) => _controls.GetValueOrDefault(windowId);

    /// <summary>The control for <paramref name="context"/>'s window, built on first use; null when the id is unregistered.</summary>
    internal IWindowControl? GetOrCreate(TerminalPanelContext context)
    {
        if (_controls.TryGetValue(context.WindowId, out var existing))
        {
            return existing;
        }

        if (!_factories.TryGetValue(context.PanelId, out var factory))
        {
            return null;
        }

        var control = factory(context);
        _controls[context.WindowId] = control;
        return control;
    }

    /// <summary>Forgets a window's control, when the window is closed.</summary>
    internal void Forget(string windowId) => _controls.Remove(windowId);
}
