using System.Text;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// Converts SharpMUTerm's UI-agnostic <see cref="StyledLine"/> model into SharpConsoleUI (Spectre-style)
/// markup: truecolor foreground/background, bold/italic/underline/etc., and clickable
/// <see cref="SpanInteraction"/>s rendered as <c>[link=…]</c> spans. Colours are resolved through the
/// active <see cref="Theme"/> so palette-indexed and default colours land on real RGB values.
/// <para>
/// <paramref name="text"/> is the app's live <see cref="TextSettings"/> — the two F7 options that are
/// decisions about *markup* rather than about the model live here (<c>allow blink</c>,
/// <c>underline hyperlinks</c>). It is held by reference and read per span, because the F7 screen
/// edits that object in place: a copy would need a restart to mean anything. Null means the defaults,
/// which is what the unit tests want.
/// </para>
/// </summary>
internal sealed class MarkupFormatter(Theme theme, TextSettings? text = null, Rgb? plane = null)
{
    private readonly Theme _theme = theme;
    private readonly TextSettings _text = text ?? new TextSettings();

    /// <summary>
    /// The plane a span carrying no background of its own is read on — computed once per formatter,
    /// because it is a property of the <em>theme</em> and not of any one pane. See
    /// <see cref="WorkspacePalette.ReadingPlane"/> for why one plane covers all fourteen a pane can
    /// wear, and why resolving it per pane would cost a whole-buffer re-format on every focus move.
    /// <para>
    /// <paramref name="plane"/> overrides it for output that is painted somewhere other than a pane —
    /// the prompt row, which sits on the idle input band. The floor is only meaningful against the
    /// fill the ink actually lands on, so a formatter writing onto a different band has to be told
    /// which one, or it holds the game's colours to a contrast they are never read at.
    /// </para>
    /// </summary>
    private readonly Rgb _plane = plane ?? WorkspacePalette.ReadingPlane(theme);

    /// <summary>Renders a whole line to a single markup string, with no timestamp gutter.</summary>
    public string ToMarkup(StyledLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return ToMarkupCore(line);
    }

    /// <summary>
    /// Renders a whole line, optionally prefixed with a dim timestamp gutter (the output view's optional
    /// timestamp column). The timestamp precedes the trigger left-rule and the styled spans.
    /// <para>
    /// The app's output panes do <em>not</em> go through this overload. They keep the stamp beside the
    /// line (<see cref="PaneLine"/>) and glue the gutter on with <see cref="WithTimestamp"/> at the
    /// moment a control is fed, so "show timestamps" is a render-time decision that repaints history
    /// rather than an append-time one that only reaches lines yet to arrive.
    /// </para>
    /// </summary>
    public string ToMarkup(StyledLine line, string? timestamp)
    {
        ArgumentNullException.ThrowIfNull(line);
        return WithTimestamp(ToMarkup(line), timestamp);
    }

    /// <summary>
    /// Prefixes already-rendered line markup with the dim timestamp gutter, or returns it untouched when
    /// there is no stamp to show. The gutter goes ahead of everything, including the trigger left-rule.
    /// </summary>
    public static string WithTimestamp(string markup, string? timestamp) =>
        string.IsNullOrEmpty(timestamp) ? markup : $"[dim]{Escape(timestamp)}[/] {markup}";

    /// <summary>Renders a whole line's own markup, with no timestamp gutter.</summary>
    private string ToMarkupCore(StyledLine line)
    {
        var sb = new StringBuilder();

        // A trigger-highlighted line gets a 2-col left rule in the trigger's colour (design output view).
        // Through the floor like every other foreground here: it lands on the pane, and it is the one
        // mark saying a rule fired at all — the demo's own teal measured 1.42:1 on the Light theme.
        if (line.RuleColor is { } rule)
        {
            var ink = _theme.Resolve(rule, isBackground: false);
            sb.Append('[')
                .Append(Hex(_text.KeepTextLegible ? Contrast.Legible(ink, _plane) : ink))
                .Append("]▌[/] ");
        }

        foreach (var span in line.Spans)
        {
            AppendSpan(sb, span);
        }

        return sb.ToString();
    }

    private void AppendSpan(StringBuilder sb, StyledSpan span)
    {
        if (span.Text.Length == 0)
        {
            return;
        }

        // Every clickable span's payload is scheme-tagged by kind, in LinkPayload, which owns both ends
        // of that round trip — see its remarks for why no kind may be passed through bare.
        var link = LinkPayload.For(span.Interaction);
        if (link is not null)
        {
            sb.Append("[link=").Append(link).Append(']');
        }

        // "underline hyperlinks": a clickable span reads as clickable even when the server sent it
        // unstyled. Added to the span's own attributes rather than emitted separately, so a link that
        // was already underlined doesn't get two tokens.
        var style = span.Style;
        if (link is not null && _text.UnderlineHyperlinks)
        {
            style = style.AddAttribute(TextAttributes.Underline);
        }

        var styleTag = StyleTag(style);
        if (styleTag is not null)
        {
            sb.Append(styleTag);
        }

        sb.Append(Escape(span.Text));

        if (styleTag is not null)
        {
            sb.Append("[/]");
        }

        if (link is not null)
        {
            sb.Append("[/]");
        }
    }

    /// <summary>Builds a markup open tag (e.g. <c>[bold #ffcc00 on #202020]</c>), or null if unstyled.</summary>
    private string? StyleTag(TextStyle style)
    {
        var reverse = style.HasAttribute(TextAttributes.Reverse);
        var fg = _theme.Resolve(style.Foreground, isBackground: false);
        var bg = _theme.Resolve(style.Background, isBackground: true);

        // Bold on a base (0–7) palette colour brightens it, matching common terminal behaviour.
        if (style.HasAttribute(TextAttributes.Bold) &&
            style.Foreground.Kind == TerminalColorKind.Indexed &&
            style.Foreground.Index < 8)
        {
            fg = _theme.ResolveIndex(style.Foreground.Index + 8);
        }

        if (reverse)
        {
            (fg, bg) = (bg, fg);
        }

        // The legibility floor, applied at the one point in this app that knows both the colour and the
        // plane it is about to be painted on. A span that carries a background is measured against *it*
        // — that plane is known exactly, and a highlight's own pair must be judged as a pair; one with
        // no background emits none (see below) and takes the pane it is drawn on, so it is measured
        // against the theme's reading plane.
        //
        // What this is for: on the default dark theme's focused pane, the ANSI colours a MU* server
        // sends constantly are blue 1.34:1, red 1.09:1, black 1.75:1 and magenta 1.27:1 — text that is
        // very nearly the surface twice. It is a *floor* and not a scheme: a colour already clearing
        // Contrast.Floor comes back byte-identical, which is most of what any game sends.
        //
        // Off restores the previous bytes exactly, for a reader who wants their game's palette
        // untouched or a theme where the floor fights their taste.
        if (_text.KeepTextLegible)
        {
            fg = Contrast.Legible(fg, style.Background.Kind == TerminalColorKind.Default && !reverse ? _plane : bg);
        }

        var tokens = new List<string>(6);
        if (style.HasAttribute(TextAttributes.Bold))
        {
            tokens.Add("bold");
        }

        if (style.HasAttribute(TextAttributes.Faint))
        {
            tokens.Add("dim");
        }

        if (style.HasAttribute(TextAttributes.Italic))
        {
            tokens.Add("italic");
        }

        if (style.HasAttribute(TextAttributes.Underline))
        {
            tokens.Add("underline");
        }

        // Blink is parsed out of SGR 5/6 but dropped unless F7's "allow blink" is on: a blinking line
        // is the one rendition a server can impose that the reader cannot stop looking at.
        if (_text.AllowBlink && style.HasAttribute(TextAttributes.Blink))
        {
            tokens.Add("blink");
        }

        if (style.HasAttribute(TextAttributes.Strikethrough))
        {
            tokens.Add("strikethrough");
        }

        tokens.Add(Hex(fg));

        // Only paint a background when one is actually set (or reverse swapped one in), so the
        // window background shows through normal text.
        if (reverse || style.Background.Kind != TerminalColorKind.Default)
        {
            tokens.Add("on " + Hex(bg));
        }

        // A foreground token is always present (the resolved fg above), so the tag is never empty.
        return $"[{string.Join(' ', tokens)}]";
    }

    private static string Hex(Rgb rgb) => $"#{rgb.R:x2}{rgb.G:x2}{rgb.B:x2}";
}
