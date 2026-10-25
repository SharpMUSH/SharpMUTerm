using Avalonia.Input;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// Avalonia's key events as the <see cref="ConsoleKeyInfo"/>s a terminal driver would have produced, so the
/// client's key tables need no second spelling of anything.
/// <para>
/// A key arrives in one of two ways. <b>A key that types nothing</b> — an arrow, a function key, Enter,
/// a Ctrl or Alt chord — is mapped from <c>KeyDown</c>. <b>A key that types a character</b> is left for
/// <c>TextInput</c>, which has already applied the keyboard layout, Shift, dead keys and the IME; mapping
/// those from the physical key would type <c>2</c> for a French <c>é</c>.
/// </para>
/// <para>
/// The spellings follow SharpConsoleUI's <c>AnsiInputParser</c>, field for field: Ctrl+letter carries its
/// control byte as the character, Alt+letter the letter itself with <c>alt</c> set, and the navigation keys
/// the characters that parser gives them. That is the shape every handler in the client was written against.
/// Where a terminal loses information this does not: Ctrl+⏎, Shift+⏎ and Ctrl+Tab arrive as themselves,
/// because a window can tell them apart.
/// </para>
/// </summary>
internal static class KeyMapper
{
    /// <summary>
    /// The console key for a <c>KeyDown</c>, or null when the key is a character the following
    /// <c>TextInput</c> will deliver.
    /// </summary>
    public static ConsoleKeyInfo? FromKeyDown(Key key, KeyModifiers modifiers)
    {
        var shift = (modifiers & KeyModifiers.Shift) != 0;
        var alt = (modifiers & KeyModifiers.Alt) != 0;
        var ctrl = (modifiers & KeyModifiers.Control) != 0;

        ConsoleKeyInfo Make(char c, ConsoleKey k) => new(c, k, shift, alt, ctrl);

        switch (key)
        {
            case Key.Enter: return Make('\r', ConsoleKey.Enter);
            case Key.Tab: return Make('\t', ConsoleKey.Tab);
            case Key.Back: return Make('\b', ConsoleKey.Backspace);
            case Key.Escape: return Make('\x1b', ConsoleKey.Escape);
            case Key.Up: return Make('\0', ConsoleKey.UpArrow);
            case Key.Down: return Make('\0', ConsoleKey.DownArrow);
            case Key.Left: return Make('\0', ConsoleKey.LeftArrow);
            case Key.Right: return Make('\0', ConsoleKey.RightArrow);
            case Key.Home: return Make('\x1b', ConsoleKey.Home);
            case Key.End: return Make('\x1b', ConsoleKey.End);
            case Key.PageUp: return Make('\x1b', ConsoleKey.PageUp);
            case Key.PageDown: return Make('\x1b', ConsoleKey.PageDown);
            case Key.Insert: return Make('\x1b', ConsoleKey.Insert);
            case Key.Delete: return Make('\x1b', ConsoleKey.Delete);
            case >= Key.F1 and <= Key.F24: return Make('\0', ConsoleKey.F1 + (key - Key.F1));
        }

        // Ctrl together with Alt is AltGr on a European layout: the character it types is TextInput's.
        if (ctrl && alt)
        {
            return null;
        }

        if (key is >= Key.A and <= Key.Z)
        {
            var letter = (char)('A' + (key - Key.A));
            var consoleKey = (ConsoleKey)letter;
            if (ctrl)
            {
                return Make((char)(letter - 'A' + 1), consoleKey);
            }

            if (alt)
            {
                return Make(shift ? letter : char.ToLowerInvariant(letter), consoleKey);
            }

            return null;
        }

        if (key is >= Key.D0 and <= Key.D9 && (ctrl || alt))
        {
            var digit = (char)('0' + (key - Key.D0));
            return Make(digit, (ConsoleKey)digit);
        }

        if (key == Key.Space && ctrl)
        {
            return Make('\0', ConsoleKey.Spacebar);
        }

        if (key == Key.Space && alt)
        {
            return Make(' ', ConsoleKey.Spacebar);
        }

        return null;
    }

    /// <summary>The console keys for the text a <c>TextInput</c> carried, one per character.</summary>
    public static IEnumerable<ConsoleKeyInfo> FromText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        foreach (var c in text)
        {
            // Control characters reach TextInput on some platforms alongside the KeyDown that already sent them.
            if (char.IsControl(c))
            {
                continue;
            }

            yield return new ConsoleKeyInfo(c, CharToConsoleKey(c), char.IsUpper(c), false, false);
        }
    }

    /// <summary>The framework's own character-to-key table (<c>AnsiInputParser.CharToConsoleKey</c>, which is internal).</summary>
    internal static ConsoleKey CharToConsoleKey(char c) => c switch
    {
        >= 'a' and <= 'z' => (ConsoleKey)(c - 'a' + 'A'),
        >= 'A' and <= 'Z' => (ConsoleKey)c,
        >= '0' and <= '9' => (ConsoleKey)c,
        ' ' => ConsoleKey.Spacebar,
        '/' => ConsoleKey.Divide,
        '*' => ConsoleKey.Multiply,
        '-' => ConsoleKey.OemMinus,
        '+' => ConsoleKey.OemPlus,
        '.' => ConsoleKey.OemPeriod,
        ',' => ConsoleKey.OemComma,
        ';' => ConsoleKey.Oem1,
        '=' => ConsoleKey.OemPlus,
        '[' => ConsoleKey.Oem4,
        ']' => ConsoleKey.Oem6,
        '\\' => ConsoleKey.Oem5,
        '\'' => ConsoleKey.Oem7,
        '`' => ConsoleKey.Oem3,
        _ => ConsoleKey.NoName,
    };
}
