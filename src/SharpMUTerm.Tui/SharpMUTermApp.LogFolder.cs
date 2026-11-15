using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Diagnostics;

namespace SharpMUTerm.Tui;

/// <summary>
/// <em>Open log folder</em>: shows the folder a character's transcripts are written to in the desktop's file manager —
/// from ⌃P (the focused window's character), the window's character page and the cell F5's <c>o</c> on a character.
/// <para>
/// The folder is <see cref="LogFolder"/>'s answer, the one <see cref="OpenLog"/> writes into, so the button opens where
/// the logs really go: the character's own folder if it names one, else the log root. <b>A folder that does not exist
/// yet is made</b>, as the first log would make it; opening an empty folder is an answer, an error is not. With no log
/// root (a test, a snapshot) it refuses, the logging gate; with no opener (<see cref="ClientHost.OpenFolder"/> null) it
/// says where the folder is instead.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The focused window's character's log folder, or the log root for a window that belongs to nobody.</summary>
    private void OpenFocusedLogFolder()
    {
        var key = SendTarget()?.SessionKey ?? _workspace.FindWindow(ActiveWindowId())?.SessionKey;
        var character = SendTarget()?.Character
            ?? (key is { Length: > 0 } && FindCharacter(key) is { } found ? found.Character : null);
        OpenLogFolder(character);
    }

    /// <summary>The log folder of the character whose session key is <paramref name="sessionKey"/>, as the settings page asks.</summary>
    private void OpenLogFolderOf(string sessionKey)
    {
        if (FindCharacter(sessionKey) is not { } found)
        {
            RefuseCommand("no such character");
            return;
        }

        OpenLogFolder(found.Character);
    }

    /// <summary>F5's <c>o</c>: the log folder of the character at those indices of the configuration.</summary>
    private void OpenLogFolderAt(int world, int character) =>
        OpenLogFolder(_config.Worlds.ElementAtOrDefault(world)?.Characters.ElementAtOrDefault(character));

    /// <summary>Makes <paramref name="character"/>'s log folder if it is missing, then shows it.</summary>
    private void OpenLogFolder(CharacterDefinition? character)
    {
        if (LogFolder(character) is not { } configured)
        {
            RefuseCommand("no log folder — this client keeps no logs");
            return;
        }

        string folder;
        try
        {
            folder = Path.GetFullPath(configured);
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Notice($"could not make the log folder {configured}: {ex.Message}", MessageSeverity.Error);
            return;
        }

        if (Host.OpenFolder is not { } open)
        {
            RefuseCommand($"this client cannot open a folder — logs are in {folder}");
            return;
        }

        try
        {
            open(folder);
            Notice($"opened {folder}", MessageSeverity.Info, toast: false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            RefuseCommand($"could not open {folder}: {ex.Message}");
        }
    }
}
