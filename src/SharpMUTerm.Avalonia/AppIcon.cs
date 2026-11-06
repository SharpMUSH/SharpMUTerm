using Avalonia.Controls;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The logo as the window's icon (<c>packaging/icons/sharpmuterm.png</c>, embedded). On Windows the executable's
/// <c>ApplicationIcon</c> already gives the taskbar one; an X11 window manager reads only what the window itself
/// sets, so without this the window shows a generic icon there. Every window this host opens sets it.
/// </summary>
internal static class AppIcon
{
    private static readonly Lazy<WindowIcon?> Icon = new(Load);

    /// <summary>The icon, or null if the resource is missing (a window then keeps the platform's default).</summary>
    public static WindowIcon? Value => Icon.Value;

    /// <summary>Sets the icon on <paramref name="window"/> when there is one.</summary>
    public static void Apply(Window window)
    {
        if (Value is { } icon)
        {
            window.Icon = icon;
        }
    }

    private static WindowIcon? Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Icons.sharpmuterm.png");
        return stream is null ? null : new WindowIcon(stream);
    }
}
