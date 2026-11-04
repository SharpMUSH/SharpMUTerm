using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;

namespace SharpMUTerm.Tui;

/// <summary>
/// Resolves a theme into the theme picker's model (<see cref="ThemePreview"/>): every plane and ink the workspace
/// would paint in that theme — the surface, the focused plane, a tinted pane, the recessed tab chips, the armed
/// command line — computed by <see cref="WorkspacePalette"/> and held to <see cref="Contrast.Floor"/> as the
/// workspace holds them. The cell picker draws it (<see cref="OptionsScreenRenderer"/>) and the window's cards draw
/// the same instance, so the two hosts cannot show a theme differently.
/// </summary>
internal static class ThemePreviews
{
    /// <summary>Every theme the picker lists, each with its preview.</summary>
    internal static IReadOnlyList<ThemeCard> Cards(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return [.. ThemeChoices.All(config).Select(Card)];
    }

    /// <summary>One theme, with its preview.</summary>
    internal static ThemeCard Card(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new ThemeCard(theme.Name, IsDark(theme), ThemeChoices.IsCustom(theme.Name), Preview(theme));
    }

    /// <summary>Whether a theme's background is darker than mid-grey.</summary>
    internal static bool IsDark(Theme theme)
    {
        var bg = theme.Resolve(TerminalColor.Default, isBackground: true);
        return (0.2126 * bg.R) + (0.7152 * bg.G) + (0.0722 * bg.B) < 128;
    }

    /// <summary>The preview of <paramref name="theme"/>.</summary>
    internal static ThemePreview Preview(Theme theme)
    {
        var surface = WorkspacePalette.Surface(theme);
        var focused = WorkspacePalette.Focus(surface);
        var tinted = WorkspacePalette.Tint(theme, ThemePreviewContent.Tint);
        var other = WorkspacePalette.Recessed(WorkspacePalette.Focus(WorkspacePalette.Tint(theme, PaneTint.Rose)));

        var panes = new[]
        {
            Pane(theme, "ANSI colours", surface, false, ThemePreviewContent.Ansi, [Tab(theme, "Aetherfall", surface, true, false)]),
            Pane(theme, "focused", focused, true, ThemePreviewContent.Focused,
            [
                Tab(theme, ThemePreviewContent.Character, focused, true, true),
                Tab(theme, "Chat", WorkspacePalette.Recessed(focused), false, false, "3"),
                Tab(theme, "Ash", other, false, false),
            ]),
            Pane(theme, $"tinted · {ThemePreviewContent.Tint.ToString().ToLowerInvariant()}", tinted, false, ThemePreviewContent.Tinted,
                [Tab(theme, ThemePreviewContent.TintedCharacter, tinted, true, false)]),
        };

        var band = WorkspacePalette.ArmedBand(theme);
        var line = new PreviewCommandLine(
            $"{ThemePreviewContent.Character} ›",
            Contrast.Legible(theme.Prompt, band),
            ThemePreviewContent.Typed,
            Contrast.Legible(theme.Resolve(TerminalColor.Default, isBackground: false), band),
            band);
        return new ThemePreview(WorkspacePalette.Backdrop(theme), panes, line);
    }

    private static PreviewPane Pane(
        Theme theme,
        string role,
        Rgb plane,
        bool focused,
        IReadOnlyList<IReadOnlyList<SampleRun>> sample,
        IReadOnlyList<PreviewTab> tabs)
    {
        var lines = sample
            .Select(runs => (IReadOnlyList<PreviewRun>)[.. runs.Select(r => new PreviewRun(r.Text, Ink(theme, r.Colour, plane), r.Bold))])
            .ToList();
        return new PreviewPane(role, plane, tabs, lines, focused);
    }

    private static PreviewTab Tab(Theme theme, string title, Rgb chip, bool selected, bool focused, string? badge = null)
    {
        var ink = selected
            ? Contrast.Legible(theme.Resolve(TerminalColor.Default, isBackground: false), chip)
            : Contrast.Legible(WorkspacePalette.IdleInk(theme), chip);
        return new PreviewTab(
            focused ? "▌" + title : title,
            chip,
            ink,
            selected,
            badge,
            badge is null ? null : Contrast.Legible(ChromeInk.BaseAccent, chip));
    }

    private static Rgb Ink(Theme theme, int? colour, Rgb plane)
    {
        var given = colour is { } index ? theme.ResolveIndex(index) : theme.Resolve(TerminalColor.Default, isBackground: false);
        return Contrast.Legible(given, plane);
    }
}
