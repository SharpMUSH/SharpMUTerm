using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;

namespace SharpMUTerm.Tui;

/// <summary>
/// The theme, changed while the client runs. F7's theme picker and the desktop's theme cards both commit through
/// <see cref="SaveConfiguration"/>, and that is where a new theme is noticed and put into force: every colour the client
/// derives from it — the workspace planes, the bars, the header, the settings screens and the popups — is re-derived and
/// repainted. What it cannot recolour is output already in a pane: a line's colours were resolved against the theme it
/// arrived under and held there as markup (<see cref="PaneLine"/>), so the notice says so rather than leaving a reader to
/// wonder why the old lines look different.
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

        _header.BackgroundColor = ToColor(theme.StatusBackground);
        _header.FocusedLinkForegroundColor = ToColor(theme.Resolve(TerminalColor.Default, isBackground: true));
        _window.ForegroundColor = ToColor(theme.Resolve(TerminalColor.Default, isBackground: false));
        foreach (var control in _panes.Values)
        {
            control.SelectionBackgroundColor = ToColor(WorkspacePalette.SelectionBand(theme));
            control.SelectionForegroundColor = ToColor(WorkspacePalette.SelectionInk(theme));
        }

        RebuildPaneArea();
        RefreshPanels();
        Notice(
            $"Theme {theme.Name}. Output already on screen keeps the colours it arrived in.",
            Core.Diagnostics.MessageSeverity.Info,
            "theme");
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
