using SharpMUTerm.Client.Settings;

namespace SharpMUTerm.Tui;

/// <summary>
/// A host that draws the declared settings with its own controls (the desktop window's settings window) instead of
/// the cell screens. The client hands it a <see cref="SettingsForm"/>; everything the form changes is committed
/// back through the client's own save funnel, so the host only draws.
/// </summary>
internal interface INativeSettings
{
    /// <summary>
    /// Shows <paramref name="form"/>, or brings an open settings window forward on its
    /// <see cref="SettingsForm.Initial"/> section. Called on the client's thread; the host marshals to its own.
    /// </summary>
    void Show(SettingsForm form);

    /// <summary>Closes the settings window if it is open. Called on the client's thread.</summary>
    void Close();
}
