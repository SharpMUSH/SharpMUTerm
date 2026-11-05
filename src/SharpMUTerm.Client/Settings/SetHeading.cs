using System.Globalization;

namespace SharpMUTerm.Client.Settings;

/// <summary>
/// The words a trigger set's heading says on the list screens (F2, F3, F4, F6), in both hosts: the set's name over its
/// items, and how many it holds. One writer, so the cell band and the window's heading cannot count a set two ways.
/// </summary>
public static class SetHeading
{
    /// <summary><c>no triggers</c>, <c>1 trigger</c>, <c>3 triggers</c>.</summary>
    public static string Count(int count, string noun, string plural) => count switch
    {
        <= 0 => $"no {plural}",
        1 => $"1 {noun}",
        _ => string.Create(CultureInfo.InvariantCulture, $"{count:N0} {plural}"),
    };
}
