namespace SharpMUTerm.Core.Text;

/// <summary>
/// Decodes an ECMA-48 SGR parameter string into a <see cref="TextStyle"/>.
/// </summary>
/// <remarks>
/// Extracted from <see cref="AnsiParser"/> so <see cref="SharpMUTerm.Core.Protocols.MxpParser"/> can
/// share it: MXP explicitly permits ANSI inside a document ("ANSI and VT100 codes can still be used
/// as normal"), and two implementations of SGR would drift the moment one gained a colour format the
/// other did not. Pure — it takes the current style and returns the next one — because the two
/// parsers keep that state in different places.
/// </remarks>
public static class SgrCodes
{
    /// <summary>
    /// Applies one SGR sequence's parameters to <paramref name="current"/>.
    /// </summary>
    /// <param name="current">The style in force before the sequence.</param>
    /// <param name="parameters">
    /// The CSI parameter string with no <c>ESC[</c> prefix and no trailing <c>m</c>, for example
    /// <c>"0;33"</c>. Empty means a reset, which is what a bare <c>ESC[m</c> encodes.
    /// </param>
    public static TextStyle Apply(TextStyle current, string parameters)
    {
        if (parameters.Length == 0)
        {
            return TextStyle.Default;
        }

        var tokens = parameters.Split(';');
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];

            // Colon-delimited extended colour, e.g. "38:5:196" or "38:2:255:0:0".
            if (token.IndexOf(':') >= 0)
            {
                current = ApplyColonColor(current, token);
                continue;
            }

            if (!int.TryParse(token, out var code))
            {
                code = 0; // An empty parameter is treated as 0 (reset).
            }

            switch (code)
            {
                case 0:
                    current = TextStyle.Default;
                    break;
                case 1:
                    current = current.AddAttribute(TextAttributes.Bold);
                    break;
                case 2:
                    current = current.AddAttribute(TextAttributes.Faint);
                    break;
                case 3:
                    current = current.AddAttribute(TextAttributes.Italic);
                    break;
                case 4:
                    current = current.AddAttribute(TextAttributes.Underline);
                    break;
                case 5:
                case 6:
                    current = current.AddAttribute(TextAttributes.Blink);
                    break;
                case 7:
                    current = current.AddAttribute(TextAttributes.Reverse);
                    break;
                case 8:
                    current = current.AddAttribute(TextAttributes.Conceal);
                    break;
                case 9:
                    current = current.AddAttribute(TextAttributes.Strikethrough);
                    break;
                case 21:
                case 22:
                    current = current.RemoveAttribute(TextAttributes.Bold | TextAttributes.Faint);
                    break;
                case 23:
                    current = current.RemoveAttribute(TextAttributes.Italic);
                    break;
                case 24:
                    current = current.RemoveAttribute(TextAttributes.Underline);
                    break;
                case 25:
                    current = current.RemoveAttribute(TextAttributes.Blink);
                    break;
                case 27:
                    current = current.RemoveAttribute(TextAttributes.Reverse);
                    break;
                case 28:
                    current = current.RemoveAttribute(TextAttributes.Conceal);
                    break;
                case 29:
                    current = current.RemoveAttribute(TextAttributes.Strikethrough);
                    break;
                case >= 30 and <= 37:
                    current = current.WithForeground(TerminalColor.FromIndex(code - 30));
                    break;
                case 38:
                    current = current.WithForeground(ParseExtendedColor(tokens, ref i) ?? current.Foreground);
                    break;
                case 39:
                    current = current.WithForeground(TerminalColor.Default);
                    break;
                case >= 40 and <= 47:
                    current = current.WithBackground(TerminalColor.FromIndex(code - 40));
                    break;
                case 48:
                    current = current.WithBackground(ParseExtendedColor(tokens, ref i) ?? current.Background);
                    break;
                case 49:
                    current = current.WithBackground(TerminalColor.Default);
                    break;
                case >= 90 and <= 97:
                    current = current.WithForeground(TerminalColor.FromIndex(code - 90 + 8));
                    break;
                case >= 100 and <= 107:
                    current = current.WithBackground(TerminalColor.FromIndex(code - 100 + 8));
                    break;
                default:
                    // Unknown SGR code — ignored.
                    break;
            }
        }

        return current;
    }

    /// <summary>
    /// Parses the semicolon-form extended colour that follows a 38/48 code, advancing
    /// <paramref name="i"/> past the consumed tokens. Returns null if malformed.
    /// </summary>
    private static TerminalColor? ParseExtendedColor(string[] tokens, ref int i)
    {
        if (i + 1 >= tokens.Length || !int.TryParse(tokens[i + 1], out var mode))
        {
            return null;
        }

        switch (mode)
        {
            case 5 when i + 2 < tokens.Length && int.TryParse(tokens[i + 2], out var idx) && idx is >= 0 and <= 255:
                i += 2;
                return TerminalColor.FromIndex(idx);

            case 2 when i + 4 < tokens.Length &&
                        byte.TryParse(tokens[i + 2], out var r) &&
                        byte.TryParse(tokens[i + 3], out var g) &&
                        byte.TryParse(tokens[i + 4], out var b):
                i += 4;
                return TerminalColor.FromRgb(r, g, b);

            default:
                return null;
        }
    }

    /// <summary>Parses a single colon-delimited extended colour token and applies it.</summary>
    private static TextStyle ApplyColonColor(TextStyle current, string token)
    {
        var parts = token.Split(':');
        if (parts.Length < 3 || !int.TryParse(parts[0], out var target))
        {
            return current;
        }

        var isForeground = target == 38;
        if (target is not (38 or 48))
        {
            return current;
        }

        if (!int.TryParse(parts[1], out var mode))
        {
            return current;
        }

        TerminalColor? colour = null;
        if (mode == 5 && int.TryParse(parts[2], out var idx) && idx is >= 0 and <= 255)
        {
            colour = TerminalColor.FromIndex(idx);
        }
        else if (mode == 2 && parts.Length >= 5)
        {
            // ISO form may carry a colour-space id at parts[2]; the RGB triple is the last three.
            var baseIndex = parts.Length >= 6 ? parts.Length - 3 : 2;
            if (byte.TryParse(parts[baseIndex], out var r) &&
                byte.TryParse(parts[baseIndex + 1], out var g) &&
                byte.TryParse(parts[baseIndex + 2], out var b))
            {
                colour = TerminalColor.FromRgb(r, g, b);
            }
        }

        if (colour is null)
        {
            return current;
        }

        return isForeground ? current.WithForeground(colour.Value) : current.WithBackground(colour.Value);
    }
}
