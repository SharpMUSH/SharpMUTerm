using System.Globalization;

namespace SharpMUTerm.Core.Configuration;

/// <summary>
/// How tall a split pane's live tail is (<see cref="TextSettings.ScrollTail"/>): a number of rows (<c>6</c>) or a
/// share of the pane (<c>25%</c>). Both hosts read and refuse it through here.
/// </summary>
public static class ScrollTail
{
    /// <summary>What a fresh configuration asks for.</summary>
    public const string Default = "6";

    /// <summary>The most rows a plain number may ask for.</summary>
    public const int MaxRows = 100;

    /// <summary>Why <paramref name="text"/> is not a tail height, or null when it is.</summary>
    public static string? Problem(string? text) =>
        TryRead(text, out _, out _) ? null : $"a number of rows from 1 to {MaxRows}, or a share such as 25%";

    /// <summary><paramref name="text"/> as it is stored: trimmed, with no space before a <c>%</c>.</summary>
    public static string Normalise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryRead(text, out var value, out var percent)
            ? value.ToString(CultureInfo.InvariantCulture) + (percent ? "%" : string.Empty)
            : text.Trim();
    }

    /// <summary>
    /// The rows <paramref name="text"/> asks for out of <paramref name="available"/> (the pane's rows less the band),
    /// or zero when it is unreadable, which the caller treats as the default.
    /// </summary>
    public static int Rows(string? text, int available)
    {
        if (!TryRead(text, out var value, out var percent) && !TryRead(Default, out value, out percent))
        {
            return 0;
        }

        return percent ? (int)Math.Round(available * value / 100.0, MidpointRounding.AwayFromZero) : value;
    }

    private static bool TryRead(string? text, out int value, out bool percent)
    {
        value = 0;
        var trimmed = text?.Trim() ?? string.Empty;
        percent = trimmed.EndsWith('%');
        var number = (percent ? trimmed[..^1] : trimmed).TrimEnd();
        if (number.Length == 0 || number.Length > 3 || !number.All(char.IsAsciiDigit))
        {
            return false;
        }

        value = int.Parse(number, NumberStyles.None, CultureInfo.InvariantCulture);
        return value >= 1 && value <= (percent ? 100 : MaxRows);
    }
}
