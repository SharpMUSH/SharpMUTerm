using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// The four colours this client paints in <em>its own</em> voice on the workspace — as
/// <c>#rrggbb</c> strings, ready to interpolate into markup. They are produced by
/// <see cref="WorkspacePalette.Chrome"/>, which holds each one to <see cref="Contrast.Floor"/>
/// against the plane it lands on.
/// <para>
/// <b>Why they are not constants any more.</b> They were, and each was picked against a dark theme
/// and then painted on whatever plane the workspace happened to have. Measured against the plane they
/// actually land on: the boundary bars' accent (<c>ResolveIndex(5)</c>, <c>#800080</c>) is
/// <b>1.27:1</b> on a focused dark pane — the reported defect — while on the Light theme the accent
/// <c>#00f5b7</c> is <b>1.42:1</b>, the draft pen <c>#ffd700</c> is <b>1.26:1</b> and the notice
/// <c>#e5c07b</c> is <b>1.73:1</b>. The Light theme's chrome has never been readable, and no snapshot
/// showed it because every frame in the gallery renders the dark theme.
/// </para>
/// <para>
/// <b>What is kept from the old hexes is the hue.</b> These are still the app's teal, its amber and
/// its gold; the theme decides how light they have to be to be seen. That is the same division the
/// pane tints already run on — a name (here, a base hue) is the durable thing, and the brightness is
/// the theme's answer, because a hex picked against one theme becomes a hole in the next.
/// </para>
/// <para>
/// <see cref="ScreenPalette"/> is deliberately <em>not</em> built this way. Those colours sit on the
/// settings screens' own fixed backdrop, which no theme moves, so measuring them against a theme plane
/// would be measuring them against a plane they are never painted on.
/// </para>
/// </summary>
/// <param name="Accent">
/// The app's teal: the rail's fallback accent, a live drop zone, the ⌃P chip, the logging indicator.
/// </param>
/// <param name="Notice">
/// The amber a client-voiced label is drawn in — <c>MOVE</c>, <c>DRAG</c>, the scrollback segment, the
/// ⌃B strip, a which-key entry's chord.
/// </param>
/// <param name="Draft">The gold pen marking a rail row whose window holds an unsent draft.</param>
/// <param name="Marker">
/// The boundary bars' accent — <c>▲ FROZEN</c>, the away bar, the restore bar. It is the one of the
/// four that is drawn from the <em>theme's</em> palette (index 5) rather than from a base hue here,
/// because a theme that overrides the base sixteen has an opinion about its own violet.
/// </param>
internal readonly record struct ChromeInk(string Accent, string Notice, string Draft, string Marker)
{
    /// <summary>The app's teal accent, before a theme has said how light it needs to be.</summary>
    internal static readonly Rgb BaseAccent = new(0x00, 0xf5, 0xb7);

    /// <summary>The amber of a client-voiced label, ditto.</summary>
    internal static readonly Rgb BaseNotice = new(0xe5, 0xc0, 0x7b);

    /// <summary>The unsent-draft pen's gold, ditto.</summary>
    internal static readonly Rgb BaseDraft = new(0xff, 0xd7, 0x00);

    /// <summary>
    /// The base hues with no plane applied — what the client painted before any of this was measured.
    /// It is what a pure renderer falls back to when no ink is handed in, which keeps those renderers
    /// callable from a unit test that has no theme and is not asking a question about colour. Nothing
    /// in the running app uses it: <c>SharpMUTermApp</c> resolves a real one from the active theme.
    /// </summary>
    internal static ChromeInk Default { get; } = new(
        BaseAccent.ToHex(),
        BaseNotice.ToHex(),
        BaseDraft.ToHex(),
        AnsiPalette.ToRgb(5).ToHex());
}
