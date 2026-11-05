using SharpMUTerm.Client.Palette;

namespace SharpMUTerm.Tui;

/// <summary>
/// A host that draws ⌃P with its own controls (the desktop window's native palette) instead of the cell popup. The
/// client hands it a <see cref="PaletteSession"/> captured on the client's thread; the host filters it with the
/// shared model, and the entry chosen goes back through the session to the cell palette's own dispatch path, so
/// the host only draws.
/// </summary>
internal interface INativePalette
{
    /// <summary>
    /// Shows <paramref name="session"/>, replacing a palette already open. Called on the client's thread; the host
    /// marshals to its own.
    /// </summary>
    void Show(PaletteSession session);

    /// <summary>Closes the palette if it is open, running nothing. Called on the client's thread.</summary>
    void Close();
}
