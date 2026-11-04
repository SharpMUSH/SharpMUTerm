using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The faces this program carries (<c>Fonts/</c>, embedded): Inter for every native view and JetBrains Mono for game
/// text, registered with Avalonia's font manager so a view naming either finds the bundled face on every OS — before
/// this, "JetBrains Mono" resolved only where it happened to be installed, and the UI face was whatever the platform's
/// default was (Segoe UI, Cantarell, San Francisco), so the window looked different on each.
/// <para>
/// Read from the assembly's manifest resources, the same streams <see cref="CellFonts"/> reads for the cells, so each
/// file is carried once. The bare family names are mapped onto the bundle (<see cref="Options"/>), so a view that says
/// <c>"JetBrains Mono"</c> or <c>"Inter"</c> gets the bundled face and an installed copy cannot replace it.
/// </para>
/// </summary>
internal sealed class BundledFonts : FontCollectionBase
{
    /// <summary>The collection's key: families in it are named <c>fonts:SharpMUTerm#Inter</c>.</summary>
    public static readonly Uri CollectionKey = new("fonts:SharpMUTerm");

    /// <summary>The bundled UI face.</summary>
    public const string Ui = "fonts:SharpMUTerm#Inter";

    /// <summary>The bundled text face, the cells' own.</summary>
    public const string Mono = "fonts:SharpMUTerm#JetBrains Mono";

    /// <summary>Every face that is registered: the four Inter weights the views use, and JetBrains Mono's four.</summary>
    internal static readonly string[] Files =
    [
        "Inter-Regular.ttf", "Inter-Medium.ttf", "Inter-SemiBold.ttf", "Inter-Bold.ttf",
        "JetBrainsMono-Regular.ttf", "JetBrainsMono-Bold.ttf", "JetBrainsMono-Italic.ttf", "JetBrainsMono-BoldItalic.ttf",
    ];

    public override Uri Key => CollectionKey;

    /// <summary>How many faces were loaded. Internal for tests: a packaging slip that drops a file shows here.</summary>
    internal int Loaded { get; }

    public BundledFonts()
    {
        foreach (var file in Files)
        {
            using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream($"Fonts.{file}");
            if (stream is not null && TryAddGlyphTypeface(stream, out _))
            {
                Loaded++;
            }
        }
    }

    /// <summary>
    /// Inter as the default family (what <see cref="FontFamily.Default"/> resolves to, so every view that names none
    /// is Inter), and the bare names mapped onto the bundle.
    /// </summary>
    public static FontManagerOptions Options => new()
    {
        DefaultFamilyName = Ui,
        FontFamilyMappings = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase)
        {
            ["Inter"] = new FontFamily(Ui),
            ["JetBrains Mono"] = new FontFamily(Mono),
        },
    };

    /// <summary>Registers the bundle and the options on an app being built: the window's and the headless tests' alike.</summary>
    public static AppBuilder Use(AppBuilder builder) =>
        builder
            .ConfigureFonts(manager => manager.AddFontCollection(new BundledFonts()))
            .With(Options);
}
