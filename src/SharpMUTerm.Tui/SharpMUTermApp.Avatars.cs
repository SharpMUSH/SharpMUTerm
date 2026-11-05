using SharpMUTerm.Core.Avatars;

namespace SharpMUTerm.Tui;

/// <summary>
/// Keeping copies of the pictures characters and worlds use (<see cref="AvatarLibrary"/>). A picture set from a file or
/// the web is read once, cut to the square the player chose (<see cref="AvatarCrop"/>; the window's crop step) or the
/// centred one, written to <c>avatars/</c> and the setting pointed at the copy, so the original can be deleted or moved
/// and a web picture is downloaded once. With no reader or no folder nothing is copied and the source is read as before.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The crop the window's crop step chose, by the source it was chosen for, until that source is copied.</summary>
    private readonly Dictionary<string, AvatarCrop> _avatarCrops = new(StringComparer.Ordinal);

    /// <summary>Sources being copied now, so a second save while one reads does not start another.</summary>
    private readonly HashSet<string> _avatarCopying = new(StringComparer.Ordinal);

    /// <summary>
    /// Records the crop the player chose for <paramref name="source"/>; the copy made of it uses that square. Called on the
    /// client's thread, before the commit that sets the source.
    /// </summary>
    internal void ChooseAvatarCrop(string source, AvatarCrop crop)
    {
        ArgumentNullException.ThrowIfNull(source);
        _avatarCrops[source.Trim()] = crop;
    }

    /// <summary>Every picture source the configuration names.</summary>
    private IEnumerable<string?> AvatarSources() =>
        _config.Worlds.SelectMany(w => w.Characters.Select(c => c.Avatar).Prepend(w.Avatar));

    /// <summary>
    /// Starts a copy for every source that is not one of the library's. On the client's thread. Each copy, once written,
    /// re-points every setting still naming its source (two characters may share one picture) and saves. The task is
    /// the copies', for a test; the client does not wait on it.
    /// </summary>
    internal Task KeepAvatarCopies()
    {
        if (_avatarCopies.Root is null || _avatars.Reader is not { } reader)
        {
            return Task.CompletedTask;
        }

        var copies = new List<Task>();

        foreach (var source in AvatarSources().Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).Distinct(StringComparer.Ordinal))
        {
            if (_avatarCopies.IsCopy(source) || AvatarSource.Problem(source) is not null || !_avatarCopying.Add(source))
            {
                continue;
            }

            var crop = _avatarCrops.TryGetValue(source, out var chosen) ? chosen : AvatarCrop.Centre;
            copies.Add(CopyAvatarAsync(source, crop, reader));
        }

        return Task.WhenAll(copies);
    }

    private async Task CopyAvatarAsync(string source, AvatarCrop crop, Func<string, CancellationToken, Task<byte[]?>> reader)
    {
        string? copy = null;
        try
        {
            if (await reader(source, CancellationToken.None).ConfigureAwait(false) is { } bytes
                && AvatarStore.Cropped(bytes, crop, AvatarLibrary.CopySize) is { } rgba)
            {
                copy = _avatarCopies.Save(PngWriter.Rgba(AvatarLibrary.CopySize, AvatarLibrary.CopySize, rgba));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            copy = null;
        }

        OnUi(() => AdoptAvatarCopy(source, copy));
    }

    /// <summary>Points every setting still naming <paramref name="source"/> at <paramref name="copy"/>, and saves.</summary>
    internal void AdoptAvatarCopy(string source, string? copy)
    {
        _avatarCopying.Remove(source);
        _avatarCrops.Remove(source);
        if (copy is null)
        {
            // Not a picture, or not answered: the setting keeps its source, which the sidebar reads (and fails on) as
            // before, so the letter stays. It is tried again at the next save or start.
            return;
        }

        var moved = false;
        foreach (var world in _config.Worlds)
        {
            if (string.Equals(world.Avatar?.Trim(), source, StringComparison.Ordinal))
            {
                world.Avatar = copy;
                moved = true;
            }

            foreach (var character in world.Characters)
            {
                if (string.Equals(character.Avatar?.Trim(), source, StringComparison.Ordinal))
                {
                    character.Avatar = copy;
                    moved = true;
                }
            }
        }

        if (!moved)
        {
            return;
        }

        // A view preference's write, not the settings funnel: nothing but the path moved, and the funnel re-periodises timers.
        PersistConfiguration();
        RefreshRail();
        RefreshHeader();
        _lastSettingsForm?.NotifyChanged();
    }
}
