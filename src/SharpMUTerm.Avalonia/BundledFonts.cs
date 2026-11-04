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
/// <para>
/// <b>The faces are made once per process and shared by every collection.</b> A font manager disposes its collections,
/// and the base collection disposes every face it holds; the headless tests set up a fresh application, and so a fresh
/// font manager and collection, for every test, so the faces were read, made and disposed once per test, hundreds of
/// times a run, and now and then one test failed to find Inter. Shared and never disposed, the eight faces are made
/// once; the window has one font manager, so nothing changes there.
/// </para>
internal sealed class BundledFonts : FontCollectionBase, IDisposable
{
    private static readonly object Gate = new();
    private static GlyphTypeface[]? s_faces;

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
        GlyphTypeface[] shared;
        lock (Gate)
        {
            if (s_faces is null)
            {
                var faces = new List<GlyphTypeface>();
                foreach (var file in Files)
                {
                    using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream($"Fonts.{file}");
                    if (stream is not null && TryAddGlyphTypeface(stream, out var face))
                    {
                        faces.Add(face);
                    }
                }

                s_faces = [.. faces];
                Loaded = s_faces.Length;
                return;
            }

            shared = s_faces;
        }

        foreach (var face in shared)
        {
            if (TryAddGlyphTypeface(face))
            {
                Loaded++;
            }
        }
    }

    /// <summary>The shared faces outlive any one font manager, so a manager disposing this collection disposes none of them.</summary>
    void IDisposable.Dispose()
    {
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
