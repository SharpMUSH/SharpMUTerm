using SharpConsoleUI.Drivers;
using SharpConsoleUI.Imaging;
using SharpMUTerm.Core.Avatars;

namespace SharpMUTerm.Tui;

/// <summary>
/// The characters' and worlds' pictures (<see cref="AvatarSource"/>), read once per source, cut to a centred square and
/// scaled to <see cref="Size"/>, and handed to the sidebar's model as one <see cref="AvatarPicture"/> per source — so a
/// model that carries a picture compares equal frame to frame and a host redraws only when it changes.
/// <para>
/// <b>Nothing is read without a reader.</b> The app's reader is caller-supplied and null by default (the
/// <c>save:</c>/<c>logRoot:</c> family), so a test or a snapshot reads no file and fetches nothing; with none every
/// lookup is null and the views draw the letters they always drew. A read that finishes at once (the demo's) is applied
/// at once, so the first frame has its pictures; one that does not is applied on the UI thread and the sidebar is told.
/// A failure is remembered for the run, so a missing file is not re-read on every frame; changing the source tries again.
/// </para>
/// </summary>
internal sealed class AvatarStore : IDisposable
{
    /// <summary>The side of every picture kept, in pixels: twice the window's largest disc, so it is sharp at 200%.</summary>
    public const int Size = 64;

    /// <summary>Sources kept, pictures and failures together; the oldest goes first.</summary>
    public const int Capacity = 128;

    private readonly Func<Action, bool> _onUi;
    private readonly Action _changed;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();

    /// <param name="onUi">Marshals onto the UI thread; returns false when it could not.</param>
    /// <param name="changed">Called on the UI thread when a picture arrives after the frame that asked for it.</param>
    public AvatarStore(Func<Action, bool> onUi, Action changed)
    {
        _onUi = onUi ?? throw new ArgumentNullException(nameof(onUi));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    /// <summary>Reads a source's bytes, or null for none; null means no picture is ever read.</summary>
    public Func<string, CancellationToken, Task<byte[]?>>? Reader { get; set; }

    /// <summary>The picture for <paramref name="source"/> once it has been read, else null (and the read is started).</summary>
    public AvatarPicture? Get(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || Reader is not { } reader)
        {
            return null;
        }

        var key = source.Trim();
        Task<byte[]?> read;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var known))
            {
                return known.Picture;
            }

            _entries[key] = new Entry(null);
            _order.AddLast(key);
            while (_order.Count > Capacity && _order.First is { } oldest)
            {
                _entries.Remove(oldest.Value);
                _order.RemoveFirst();
            }
        }

        try
        {
            read = reader(key, _cts.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }

        if (read.IsCompleted)
        {
            return Settle(key, read);
        }

        _ = read.ContinueWith(
            done =>
            {
                var picture = Settle(key, done);
                if (picture is not null && !_cts.IsCancellationRequested)
                {
                    _onUi(_changed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return null;
    }

    private AvatarPicture? Settle(string key, Task<byte[]?> read)
    {
        var bytes = read.IsCompletedSuccessfully ? read.Result : null;
        var picture = bytes is null ? null : Picture(key, bytes);
        lock (_gate)
        {
            if (_entries.ContainsKey(key))
            {
                _entries[key] = new Entry(picture);
            }
        }

        return picture;
    }

    /// <summary>Decodes, crops to the centred square and scales; null when the bytes are not a picture.</summary>
    internal static AvatarPicture? Picture(string source, byte[] bytes) =>
        Cropped(bytes, AvatarCrop.Centre, Size) is { } rgba ? new AvatarPicture(source, Size, rgba) : null;

    /// <summary>
    /// The square <paramref name="crop"/> names, scaled to <paramref name="size"/>², as opaque RGBA; null when the bytes
    /// are not a picture. The one cut both the sidebar's pictures and the kept copies are made with.
    /// </summary>
    internal static byte[]? Cropped(byte[] bytes, AvatarCrop crop, int size)
    {
        PixelBuffer decoded;
        try
        {
            decoded = ImageDecoder.Decode(bytes);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }

        if (decoded.Width <= 0 || decoded.Height <= 0)
        {
            return null;
        }

        var (left, top, side) = crop.In(decoded.Width, decoded.Height);
        var square = new PixelBuffer(side, side);
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                square.SetPixel(x, y, decoded.GetPixel(left + x, top + y));
            }
        }

        var scaled = PixelScale.Stretch(square, size, size);
        var rgba = new byte[size * size * 4];
        var i = 0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var p = scaled.GetPixel(x, y);
                rgba[i++] = p.R;
                rgba[i++] = p.G;
                rgba[i++] = p.B;
                rgba[i++] = 255;
            }
        }

        return rgba;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private sealed record Entry(AvatarPicture? Picture);
}

/// <summary>
/// The pictures the cell rail has sent to a Kitty terminal: each avatar twice, as drawn and greyed for a character who is
/// not connected (a placeholder cell cannot be dimmed), under ids of its own, sent once per run and reused.
/// </summary>
internal sealed class AvatarKittyImages
{
    /// <summary>
    /// The first id handed out: above the inline pictures' (<see cref="InlineImageHost.FirstKittyId"/>, numbered up from
    /// 0xC00000) by a margin no session reaches, and still inside the 24 bits a placeholder's colour carries.
    /// </summary>
    public const int FirstId = 0xF00000;

    /// <summary>Cells a picture covers on a rail row: two columns of one row, about square.</summary>
    public const int Columns = 2;

    private readonly Dictionary<(AvatarPicture Picture, bool Dim), int> _ids = new();
    private int _next = FirstId;

    /// <summary>
    /// The id <paramref name="picture"/> is shown under (sent now if it never was), or null when it cannot be sent.
    /// Called on the UI thread, before the rows naming it are painted.
    /// </summary>
    public int? IdFor(AvatarPicture picture, bool dim, IGraphicsProtocol? protocol)
    {
        if (protocol is null)
        {
            return null;
        }

        if (_ids.TryGetValue((picture, dim), out var known))
        {
            return known;
        }

        if (_next > 0xFFFFFF)
        {
            return null;
        }

        var id = _next++;
        try
        {
            protocol.TransmitRawRgb((uint)id, Rgb(picture, dim), picture.Size, picture.Size, Columns, 1);
        }
        catch (NotSupportedException)
        {
            return null;
        }

        _ids[(picture, dim)] = id;
        return id;
    }

    /// <summary>The picture as RGB, greyed and darkened when <paramref name="dim"/>.</summary>
    internal static byte[] Rgb(AvatarPicture picture, bool dim)
    {
        var pixels = picture.Size * picture.Size;
        var rgb = new byte[pixels * 3];
        for (var i = 0; i < pixels; i++)
        {
            var r = picture.Rgba[i * 4];
            var g = picture.Rgba[(i * 4) + 1];
            var b = picture.Rgba[(i * 4) + 2];
            if (dim)
            {
                var grey = (byte)(((r * 299) + (g * 587) + (b * 114)) / 1000 * 55 / 100);
                r = g = b = grey;
            }

            rgb[i * 3] = r;
            rgb[(i * 3) + 1] = g;
            rgb[(i * 3) + 2] = b;
        }

        return rgb;
    }
}
