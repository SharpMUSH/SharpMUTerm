using SharpMUTerm.Client.Surfaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// The model each declared surface's cells were last drawn from (<see cref="SurfaceCatalog"/>), by surface and by
/// window. The cell renderers record what they were handed; the window suite compares it with what the host was
/// told in the same frame, so a native view drawn from anything but the model the cells drew fails a test rather
/// than drifting.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private readonly Dictionary<(string Surface, string Key), object> _drawnModels = new();

    /// <summary>Records <paramref name="model"/> as the one <paramref name="surface"/>'s cells are about to draw.</summary>
    private T Drawing<T>(string surface, string key, T model)
        where T : notnull
    {
        _drawnModels[(surface, key)] = model;
        return model;
    }

    /// <summary>
    /// The model a surface's cells were last drawn from: <paramref name="key"/> is the window id for a panel, empty
    /// for the header and the sidebar. Null when they have not been drawn. Internal for tests.
    /// </summary>
    internal object? DrawnModel(string surface, string key = "") =>
        surface == SurfaceCatalog.Palette ? (_palette.IsOpen ? _palette.View : null)
        : surface == SurfaceCatalog.Prefix ? (_prefixPanel.IsOpen ? _prefixPanel.Drawn : null)
        : surface == SurfaceCatalog.Compose ? (_composer.IsOpen ? _composer.Model : null)
        : _drawnModels.TryGetValue((surface, key), out var model) ? model : null;

    /// <summary>A click on a header link, as the header's own handler receives it.</summary>
    internal void SimulateHeaderLink(string url) => OnChromeLinkClicked(url);

    /// <summary>The notice on the status row right now, as markup, or null; for a test comparing two paths' outcomes.</summary>
    internal string? LastNotice => _notice;

    /// <summary>Each session's walk — under way or not, and the rooms still ahead — for a test comparing two paths.</summary>
    internal string WalkState => string.Join(
        ";",
        _recorders.Select(r => (r.Key.SessionKey, r.Value))
            .Concat(_demoMap is null ? [] : [(DemoScene.ActiveSessionKey, _demoMap)])
            .Select(r => $"{r.Item1}:{r.Item2.Walk.IsWalking}:{string.Join('/', r.Item2.Walk.RoomsAhead)}")
            .Order(StringComparer.Ordinal));

    /// <summary>Types <paramref name="query"/> into the open cell palette, as if it had been keyed in.</summary>
    internal void SimulateCellPaletteQuery(string query) => _palette.SetQuery(query);

    /// <summary>
    /// Types <paramref name="query"/> into the open cell palette and commits the entry with <paramref name="id"/>, as
    /// arrows and ⏎ would. False when the palette is closed or does not list it.
    /// </summary>
    internal bool SimulateCellPaletteChoice(string query, string id)
    {
        if (!_palette.IsOpen)
        {
            return false;
        }

        _palette.SetQuery(query);
        return _palette.Choose(id);
    }
}
