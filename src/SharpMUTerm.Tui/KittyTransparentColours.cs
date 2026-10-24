using System.Globalization;
using System.Text;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// The escape that tells kitty to draw a named background colour translucent: its color-control code,
/// <c>OSC 21 ; transparent_background_colorN=#rrggbb@opacity ST</c>, one key per slot.
/// <para>
/// <b>This is the only way an application can put a level on its own backgrounds.</b> A translucent
/// terminal draws translucent only the cells written on its <em>default</em> background (<c>SGR 49</c>),
/// at the opacity in its configuration, and every explicit colour opaque — so a client that wants its
/// pane planes, chips and command line at sixty percent has to name those colours to the terminal and
/// say how opaque each one is. Kitty gives exactly seven slots for that (0.36 and later) and matches a
/// cell by its exact colour, which is why the planes are named and not "everything this client paints".
/// </para>
/// <para>
/// A slot the client does not use is <em>reset</em> (the key alone, no <c>=</c>), which restores what the
/// user's own <c>kitty.conf</c> said rather than clearing it; the same reset on every slot is what the
/// client sends on the way out, so it never leaves a kitty window drawing its colours translucent.
/// </para>
/// </summary>
public static class KittyTransparentColours
{
    /// <summary>How many colours kitty can hold — <c>transparent_background_color1</c> to <c>7</c>.</summary>
    public const int Slots = 7;

    private const string Osc = "\u001b]21";
    private const string St = "\u001b\\";
    private const string Key = "transparent_background_color";

    /// <summary>
    /// Sets the first <see cref="Slots"/> of <paramref name="planes"/> to the opacity a transparency of
    /// <paramref name="transparency"/> percent leaves, and resets every slot after them.
    /// </summary>
    public static string Sequence(IReadOnlyList<Rgb> planes, int transparency)
    {
        ArgumentNullException.ThrowIfNull(planes);
        var opacity = Opacity(transparency).ToString("0.##", CultureInfo.InvariantCulture);
        var builder = new StringBuilder(Osc);
        for (var slot = 0; slot < Slots; slot++)
        {
            builder.Append(';').Append(Key).Append(slot + 1);
            if (slot < planes.Count)
            {
                builder.Append('=').Append(planes[slot].ToHex()).Append('@').Append(opacity);
            }
        }

        return builder.Append(St).ToString();
    }

    /// <summary>Every slot back to what the user's own configuration says.</summary>
    public static string Reset { get; } = Sequence(Array.Empty<Rgb>(), 0);

    /// <summary>Opacity is what kitty takes; transparency is what the setting says. One is the other's complement.</summary>
    public static double Opacity(int transparency) =>
        1.0 - Math.Clamp(transparency, 0, 100) / 100.0;
}
