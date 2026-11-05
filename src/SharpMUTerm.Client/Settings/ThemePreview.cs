using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Client.Settings;

/// <summary>One run of preview text and the colour it is drawn in, already held to the legibility floor on its pane.</summary>
/// <param name="Text">The text.</param>
/// <param name="Ink">Its colour.</param>
/// <param name="Bold">Whether it is bold.</param>
public sealed record PreviewRun(string Text, Rgb Ink, bool Bold = false);

/// <summary>One tab of a preview pane's strip.</summary>
/// <param name="Title">The window's name.</param>
/// <param name="Chip">The chip it is painted on.</param>
/// <param name="Ink">Its text colour on the chip.</param>
/// <param name="Selected">Whether it is the tab in front.</param>
/// <param name="Badge">An unread count, or null.</param>
/// <param name="BadgeInk">The count's colour on the chip.</param>
public sealed record PreviewTab(string Title, Rgb Chip, Rgb Ink, bool Selected, string? Badge = null, Rgb? BadgeInk = null);

/// <summary>One preview pane: what it stands for, its plane, its tab strip and its lines.</summary>
/// <param name="Role">What the pane shows about the theme (<c>ANSI colours</c>, <c>focused</c>, <c>tinted · moss</c>).</param>
/// <param name="Plane">The pane's surface.</param>
/// <param name="Tabs">Its tab strip.</param>
/// <param name="Lines">Its output, line by line.</param>
/// <param name="Focused">Whether it is the pane holding the keyboard.</param>
public sealed record PreviewPane(
    string Role, Rgb Plane, IReadOnlyList<PreviewTab> Tabs, IReadOnlyList<IReadOnlyList<PreviewRun>> Lines, bool Focused);

/// <summary>The armed command line under the panes.</summary>
/// <param name="Prompt">The prompt, <c>Corvid ›</c>.</param>
/// <param name="PromptInk">The prompt's colour.</param>
/// <param name="Text">The line being typed.</param>
/// <param name="Ink">Its colour.</param>
/// <param name="Band">The band it is drawn on.</param>
public sealed record PreviewCommandLine(string Prompt, Rgb PromptInk, string Text, Rgb Ink, Rgb Band);

/// <summary>
/// A theme as the theme picker shows it: three panes on the workspace's backdrop — a world's ANSI colours, the focused
/// pane, a character's tinted pane — each with its tab strip, and the command line under them. Every colour is
/// resolved by the client from the theme exactly as it paints the workspace, so the cell picker and the native cards
/// draw one instance and cannot disagree. The words are <see cref="ThemePreviewContent"/>'s.
/// </summary>
/// <param name="Backdrop">The workspace backdrop the panes sit on.</param>
/// <param name="Panes">The panes, left to right.</param>
/// <param name="CommandLine">The command line.</param>
public sealed record ThemePreview(Rgb Backdrop, IReadOnlyList<PreviewPane> Panes, PreviewCommandLine CommandLine);

/// <summary>A theme the picker lists, with its preview.</summary>
/// <param name="Name">The theme's name, which is what is stored.</param>
/// <param name="IsDark">Whether its background is dark.</param>
/// <param name="IsCustom">Whether it is one of the player's own rather than a built-in.</param>
/// <param name="Preview">How it looks.</param>
public sealed record ThemeCard(string Name, bool IsDark, bool IsCustom, ThemePreview Preview);

/// <summary>A run of sample text: its words and the ANSI colour (0–15) a world would send it in, or null for the default.</summary>
/// <param name="Text">The words.</param>
/// <param name="Colour">An ANSI index, or null for the pane's default foreground.</param>
/// <param name="Bold">Whether it is bold.</param>
public sealed record SampleRun(string Text, int? Colour = null, bool Bold = false);

/// <summary>
/// What every theme preview says, whatever its colours: the words both hosts draw. Kept here, beside the model, so a
/// change to the sample is one change for both. Short lines, because a cell preview splits the screen in three.
/// </summary>
public static class ThemePreviewContent
{
    /// <summary>The character the preview's focused pane and command line belong to.</summary>
    public const string Character = "Corvid";

    /// <summary>The second character, whose pane wears a tint.</summary>
    public const string TintedCharacter = "Wren";

    /// <summary>The tint the second character's pane wears.</summary>
    public const PaneTint Tint = PaneTint.Moss;

    /// <summary>What the focused pane's command line holds.</summary>
    public const string Typed = "say Well met.";

    /// <summary>The pane showing a world's ANSI colours.</summary>
    public static IReadOnlyList<IReadOnlyList<SampleRun>> Ansi { get; } =
    [
        [new("The Grand Plaza", Bold: true)],
        [new("Exits: "), new("north", 6), new(", "), new("east", 6)],
        [new("Mara", 2), new(" says, "), new("\"Hi!\"", 3)],
        [new("<OOC> ", 5), new("Tam: brb")],
        [new("HP ", 1), new("412", 9), new("/500 "), new("MP ", 4), new("88", 12)],
    ];

    /// <summary>The focused pane.</summary>
    public static IReadOnlyList<IReadOnlyList<SampleRun>> Focused { get; } =
    [
        [new("You enter the hall.")],
        [new("A lantern burns", 3), new(" low.")],
        [new("Ilse", 2), new(" waves.")],
        [new("[Chat] ", 13), new("Ash: hello")],
        [new("> ", 8), new("look")],
    ];

    /// <summary>The tinted pane.</summary>
    public static IReadOnlyList<IReadOnlyList<SampleRun>> Tinted { get; } =
    [
        [new("Wren pages: ready?")],
        [new("You page Wren.", 8)],
        [new("The bell tolls.", 11)],
        [new("Rain on the roof.")],
        [new("* ", 10), new("Wren nods.")],
    ];
}
