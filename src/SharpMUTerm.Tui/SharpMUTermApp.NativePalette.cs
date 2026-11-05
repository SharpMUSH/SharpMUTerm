using SharpMUTerm.Client.Palette;

namespace SharpMUTerm.Tui;

/// <summary>
/// ⌃P in a host that draws it natively (<see cref="ClientHost.NativePalette"/>). Every way to the palette — ⌃P, the
/// header's brand and search box, the pointer menu's <em>More commands…</em> — ends in <see cref="ToggleMenu"/> or
/// <see cref="OpenMenu"/>, and those ask the host first. A host without one (the terminal, the snapshot, the tests)
/// gets the cell popup, unchanged.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The session the host's palette is showing, or null while it is closed. Read on the client's thread.</summary>
    private PaletteSession? _nativePaletteSession;

    /// <summary>Whether either palette is up: the cell popup or the host's own.</summary>
    private bool PaletteIsOpen => _palette.IsOpen || _nativePaletteSession is not null;

    /// <summary>What SUGGESTED lists right now, in either palette.</summary>
    internal IReadOnlyList<string> SuggestedCommandIds => CommandPaletteModel.Suggested(CommandState());

    /// <summary>Whether the host's palette is up, for a test.</summary>
    internal bool NativePaletteIsOpen => _nativePaletteSession is not null;

    /// <summary>
    /// Opens ⌃P if it is not up — never closes it. For a search box or a menu entry, which is chosen to start
    /// searching, never to stop.
    /// </summary>
    /// <param name="query">What the palette opens with typed in; null for nothing.</param>
    internal void OpenMenu(string? query = null)
    {
        if (PaletteIsOpen)
        {
            return;
        }

        _openingQuery = query;
        try
        {
            ToggleMenu();
        }
        finally
        {
            _openingQuery = null;
        }

        if (query is not null && _palette.IsOpen)
        {
            _palette.SetQuery(query);
        }
    }

    /// <summary>The query <see cref="OpenMenu"/> asked for, while it is opening the palette.</summary>
    private string? _openingQuery;

    /// <summary>
    /// Shows the palette in the host's own controls; false when the host has none. The catalog, recents and
    /// suggestions are captured here, on the client's thread, so the host can filter on its own. The entry it runs
    /// comes back through <see cref="RunFromPalette"/>, the cell palette's own path.
    /// </summary>
    private bool TryOpenNativePalette()
    {
        if (Host.NativePalette is not { } native)
        {
            return false;
        }

        PaletteSession? session = null;
        session = new PaletteSession(
            BuildCatalog(),
            _recentCommands.Ids,
            SuggestedCommandIds,
            _active is { } acting ? DisplayName(acting.SessionKey) : null,
            ThemeIsDark(),
            _openingQuery ?? string.Empty,
            id => OnUiThread(() => RunFromPalette(id)),
            () => OnUiThread(() => NativePaletteClosed(session!)))
        {
            Theme = _theme.Clone(),
        };
        _nativePaletteSession = session;
        native.Show(session);
        return true;
    }

    /// <summary>Closes the host's palette, running nothing.</summary>
    private void CloseNativePalette()
    {
        if (_nativePaletteSession is { } session)
        {
            session.Dismiss();
            Host.NativePalette?.Close();
        }
    }

    /// <summary>The host's palette has closed; a session an earlier opening left behind is ignored.</summary>
    private void NativePaletteClosed(PaletteSession session)
    {
        if (!ReferenceEquals(_nativePaletteSession, session))
        {
            return;
        }

        _nativePaletteSession = null;
        _header.SetContent(new List<string> { HeaderMarkup() });
    }
}
