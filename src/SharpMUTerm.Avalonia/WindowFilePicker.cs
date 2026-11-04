using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The desktop's own file picker, for an import from another client (<see cref="ClientHost.FilePicker"/>). It hands the
/// client a local path and nothing else: what the file holds is read, bounded and reviewed by the client, the same as a
/// path typed after <c>/import</c>. Not available until the main window attaches it, so a test, the parity harness and
/// <c>--render-png</c> are told to type a path instead.
/// </summary>
internal sealed class WindowFilePicker : IFilePicker
{
    private TopLevel? _owner;

    public bool Available => Volatile.Read(ref _owner) is not null;

    /// <summary>The window the picker opens over.</summary>
    public void Attach(TopLevel owner) => Volatile.Write(ref _owner, owner);

    public void Pick(string title, Action<string?> picked)
    {
        ArgumentNullException.ThrowIfNull(picked);
        Dispatcher.UIThread.Post(async () =>
        {
            string? path = null;
            try
            {
                if (_owner?.StorageProvider is { CanOpen: true } storage)
                {
                    var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = title,
                        AllowMultiple = false,
                        FileTypeFilter =
                        [
                            new FilePickerFileType("Client setups") { Patterns = ["*.mcl", "*.xml", "*.mpackage", "*.zip", "*.tin", "*.tt", "*.txt"] },
                            FilePickerFileTypes.All,
                        ],
                    });
                    path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
                }
            }
            catch (InvalidOperationException)
            {
                path = null;
            }

            picked(path);
        });
    }
}
