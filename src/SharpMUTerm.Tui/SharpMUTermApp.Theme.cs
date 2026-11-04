using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;

namespace SharpMUTerm.Tui;

/// <summary>
/// The theme, changed while the client runs. F7's theme picker and the desktop's theme cards both commit through
/// <see cref="SaveConfiguration"/>, and that is where a new theme is noticed and put into force: every colour the client
/// derives from it — the workspace planes, the bars, the header, the settings screens and the popups — is re-derived and
/// repainted — output already in a pane included: every world line is kept beside its markup
/// (<see cref="PaneLine.Source"/>) and formatted again against the new theme (<see cref="RestylePaneLines"/>).
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Screen palettes for themes previewed on F7, by name, so moving the cursor does not re-derive one per key.</summary>
    private readonly Dictionary<string, ScreenTheme> _previewSchemes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The theme in force, for a test.</summary>
    internal string ThemeName => _theme.Name;

    /// <summary>
    /// The palette F7 is painted in while the theme picker's cursor is on <paramref name="preview"/>: that theme's, which
    /// is the live preview; otherwise the theme in force.
    /// </summary>
    private ScreenTheme SchemeFor(string? preview)
    {
        if (preview is null || string.Equals(preview, _theme.Name, StringComparison.OrdinalIgnoreCase))
        {
            return _screens;
        }

        if (!_previewSchemes.TryGetValue(preview, out var scheme))
        {
            scheme = ThemeChoices.Find(_config, preview) is { } theme ? ScreenTheme.For(theme) : _screens;
            _previewSchemes[preview] = scheme;
        }

        return scheme;
    }

    /// <summary>Puts the configuration's theme into force when it is not the one the client is painting in.</summary>
    private void ApplyThemeIfChanged()
    {
        var theme = ThemeChoices.Resolve(_config);
        if (!string.Equals(Signature(theme), Signature(_theme), StringComparison.Ordinal))
        {
            ApplyTheme(theme);
        }
    }

    /// <summary>
    /// Hands the workspace popups — ⌃P, the quit prompt, ⌃F, ⌃R, the ⌃B panel, the game data and the message log — the
    /// settings screens' palette for the active theme (<see cref="ScreenTheme"/>), so they follow Light and Solarized
    /// as the settings screens do rather than painting the Dark theme's navy over a light workspace. Each resolves its
    /// plane, frame and markup through it the next time it opens.
    /// </summary>
    private void SchemePopups()
    {
        _palette.Scheme = _screens;
        _quit.Scheme = _screens;
        _search.Scheme = _screens;
        _historySearch.Scheme = _screens;
        _prefixPanel.Scheme = _screens;
        _gameData.Scheme = _screens;
        _messageLog.Scheme = _screens;
    }

    /// <summary>
    /// Re-derives everything the client paints from <paramref name="theme"/>. The caller (<see cref="SaveConfiguration"/>)
    /// goes on to repaint the header, the rail, the pane planes, the bars and the backdrop, which all read
    /// <see cref="_theme"/> as they paint; this does the parts that were resolved once at start.
    /// </summary>
    private void ApplyTheme(Theme theme)
    {
        _theme = theme;
        _ink = WorkspacePalette.Chrome(theme);
        _formatter = new MarkupFormatter(theme, _config.Text);
        _promptFormatter = new MarkupFormatter(theme, _config.Text, WorkspacePalette.IdleBand(theme));
        _screens = ScreenTheme.For(theme);
        _previewSchemes.Clear();

        _settings.Scheme = _screens;
        _composer.Scheme = _screens;
        _packageView.Scheme = _screens;
        _sendMenu.Scheme = _screens;
        _layoutsView.Scheme = _screens;
        _welcomeView.Scheme = _screens;
        SchemePopups();

        _header.BackgroundColor = ToColor(theme.StatusBackground);
        _header.FocusedLinkForegroundColor = ToColor(theme.Resolve(TerminalColor.Default, isBackground: true));
        var brand = AccentPalette[2];
        _header.FocusedLinkBackgroundColor = ToColor(WorkspacePalette.HeaderFill(theme, new Rgb(brand.R, brand.G, brand.B)));
        _window.ForegroundColor = ToColor(theme.Resolve(TerminalColor.Default, isBackground: false));
        foreach (var control in _panes.Values)
        {
            control.SelectionBackgroundColor = ToColor(WorkspacePalette.SelectionBand(theme));
            control.SelectionForegroundColor = ToColor(WorkspacePalette.SelectionInk(theme));
        }

        RestylePaneLines();
        RebuildPaneArea();
        RefreshPanels();
        Notice($"Painting in {theme.Name}, output on screen included.", Core.Diagnostics.MessageSeverity.Info, "theme");
    }

    /// <summary>
    /// Formats every buffered world line again against the theme in force, redraws every bar in its accent, and
    /// re-feeds the panes. A line's colours were resolved against the reading plane of the theme it arrived under
    /// (<see cref="Contrast"/>, <see cref="MarkupFormatter"/>), so without this a switch from Dark to Light left the
    /// history in greys lifted for a dark plane — faint on a light one until it scrolled away.
    /// <para>
    /// <b>Nothing moves.</b> Each row is replaced in place, so the count and the order of every buffer are what they
    /// were, and every index into one — the freeze point, the away and activity marks, the <c>NEW</c> boundary, the
    /// search bar, a picture's anchor — still names the row it named. The plain text is not touched (it is the same
    /// text), so ⌃F finds what it found. A row with no <see cref="PaneLine.Source"/> (a picture's rows, a client
    /// reply, the demo's numbered lines) keeps its markup: a picture's pixels are not the theme's, and a reply is
    /// <c>[dim]</c>, which the framework resolves to one grey whatever the theme. The re-feed keeps the selection,
    /// because only colours changed.
    /// </para>
    /// <para>
    /// This is the whole-buffer path the timestamp toggle uses (<see cref="RepaintPanes"/>), bounded by the same kind
    /// of event: one deliberate commit of a new theme.
    /// </para>
    /// </summary>
    internal void RestylePaneLines()
    {
        var accent = FrozenAccentHex();
        foreach (var buffer in _lines.Values)
        {
            for (var i = 0; i < buffer.Count; i++)
            {
                var line = buffer[i];
                if (line.Bar is { } bar)
                {
                    buffer[i] = line with { Markup = bar(accent) };
                }
                else if (line.Source is { } source)
                {
                    if (line.Pictures is { } pictures)
                    {
                        pictures.Restyle(_formatter.ToMarkupPieces(source, pictures.Cuts));
                        buffer[i] = line with { Markup = pictures.Compose() };
                    }
                    else
                    {
                        buffer[i] = line with { Markup = _formatter.ToMarkup(source) };
                    }
                }
            }
        }

        RepaintPanes(keepSelection: true);
    }

    /// <summary>A theme's name and colours as one string, so two resolutions of one theme compare equal.</summary>
    private static string Signature(Theme theme) => string.Join(
        ' ',
        new[]
        {
            theme.Name, theme.Foreground.ToHex(), theme.Background.ToHex(), theme.StatusForeground.ToHex(),
            theme.StatusBackground.ToHex(), theme.Border.ToHex(), theme.SystemMessage.ToHex(), theme.LocalEcho.ToHex(),
            theme.Prompt.ToHex(),
        }.Concat(theme.Palette16?.Select(c => c.ToHex()) ?? []));
}
