using SharpConsoleUI.Drivers;
using SharpConsoleUI.Imaging;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui;

/// <summary>
/// Turns the MXP <c>&lt;IMAGE&gt;</c> requests on a line into pane rows: fetch, decode, size, and —
/// under Kitty — transmit, then hand the rows back on the UI thread to be inserted under the line.
/// <para>
/// Conservative in the ways a world could abuse it. Fetching goes through <see cref="WebImageLoader"/>'s
/// rules (http(s) and <c>data:</c>, an image content type, a byte cap, a timeout). At most
/// <see cref="MaxConcurrentFetches"/> run at once and at most <see cref="MaxQueuedLines"/> lines wait,
/// so a server spamming tags gets placeholders rather than a client with a hundred sockets open. A
/// result is kept per URL, presentation and box, so the room map a game re-sends on every <c>look</c>
/// is fetched once and, under Kitty, transmitted once — later copies are placeholder cells pointing at
/// the image the terminal already holds.
/// </para>
/// </summary>
internal sealed class InlineImageHost : IDisposable
{
    /// <summary>Simultaneous fetches across every session.</summary>
    public const int MaxConcurrentFetches = 2;

    /// <summary>Lines whose images may be waiting at once; past this a line keeps its placeholder.</summary>
    public const int MaxQueuedLines = 16;

    /// <summary>Finished results kept for reuse.</summary>
    public const int CacheSize = 32;

    /// <summary>
    /// First Kitty image id this host hands out. The framework numbers its own images (the web view's)
    /// from 1; starting high keeps the two apart, and every id still fits the 24 bits a placeholder's
    /// foreground colour can carry.
    /// </summary>
    public const int FirstKittyId = 0xC00000;

    private readonly Func<string, CancellationToken, Task<byte[]?>> _fetch;
    private readonly Action<Action> _onUi;
    private readonly SemaphoreSlim _fetches = new(MaxConcurrentFetches);
    private readonly CancellationTokenSource _cts = new();
    private readonly LinkedList<(string Key, IReadOnlyList<string> Rows)> _cache = new();
    private readonly object _gate = new();
    private int _queued;
    private int _nextKittyId = FirstKittyId;

    /// <param name="fetch">Fetches an image's bytes, or null.</param>
    /// <param name="onUi">Marshals onto the UI thread.</param>
    public InlineImageHost(Func<string, CancellationToken, Task<byte[]?>> fetch, Action<Action> onUi)
    {
        _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
        _onUi = onUi ?? throw new ArgumentNullException(nameof(onUi));
    }

    private readonly HashSet<Task> _running = new();

    /// <summary>Completes when every load started so far has finished — for tests and snapshots.</summary>
    public Task Pending
    {
        get
        {
            lock (_gate)
            {
                return Task.WhenAll(_running.ToArray());
            }
        }
    }

    /// <summary>The image requests a line carries, in order.</summary>
    public static IReadOnlyList<InlineImageRequest> RequestsIn(StyledLine line)
    {
        List<InlineImageRequest>? found = null;
        foreach (var span in line.Spans)
        {
            if (span.Interaction?.Image is { } request)
            {
                (found ??= new()).Add(request);
            }
        }

        return found ?? (IReadOnlyList<InlineImageRequest>)Array.Empty<InlineImageRequest>();
    }

    /// <summary>
    /// Loads a line's pictures in order and delivers each one's rows through <paramref name="deliver"/>
    /// on the UI thread. <paramref name="kitty"/> is asked on the UI thread, at delivery: it is the
    /// driver, and the transmit has to happen just before the rows that point at it are painted.
    /// </summary>
    /// <returns>False when the queue is full and nothing was started.</returns>
    public bool Load(
        IReadOnlyList<InlineImageRequest> requests,
        InlineImagePresentation presentation,
        int availableColumns,
        Func<IGraphicsProtocol?> kitty,
        Action<IReadOnlyList<string>> deliver)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(kitty);
        ArgumentNullException.ThrowIfNull(deliver);
        if (requests.Count == 0 || availableColumns <= 0 ||
            presentation is not (InlineImagePresentation.Kitty or InlineImagePresentation.HalfBlock))
        {
            return false;
        }

        lock (_gate)
        {
            if (_queued >= MaxQueuedLines)
            {
                return false;
            }

            _queued++;
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? task = null;
        task = Task.Run(async () =>
        {
            await started.Task.ConfigureAwait(false);
            try
            {
                foreach (var request in requests)
                {
                    var rows = await RowsForAsync(request, presentation, availableColumns, kitty).ConfigureAwait(false);
                    if (rows is not null && !_cts.IsCancellationRequested)
                    {
                        _onUi(() => deliver(rows));
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                // The client is closing; the line keeps its link.
            }
            finally
            {
                lock (_gate)
                {
                    _queued--;
                    _running.Remove(task!);
                }
            }
        });

        lock (_gate)
        {
            _running.Add(task);
        }

        started.SetResult();
        return true;
    }

    private async Task<IReadOnlyList<string>?> RowsForAsync(
        InlineImageRequest request,
        InlineImagePresentation presentation,
        int availableColumns,
        Func<IGraphicsProtocol?> kitty)
    {
        var key = $"{presentation}|{availableColumns}|{request.Width}|{request.Height}|{request.Url}";
        if (Cached(key) is { } hit)
        {
            return hit;
        }

        byte[]? bytes;
        await _fetches.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            bytes = await _fetch(request.Url, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _fetches.Release();
        }

        var picture = bytes is null ? null : Decode(bytes, request, availableColumns);
        if (picture is null)
        {
            return null;
        }

        IReadOnlyList<string>? rows = null;
        if (presentation == InlineImagePresentation.Kitty)
        {
            // The transmit is a write to the terminal and the driver serialises it against painting;
            // it is done on the UI thread so the image is there before the rows naming it are drawn.
            var done = new TaskCompletionSource<IReadOnlyList<string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _onUi(() => done.TrySetResult(Transmit(picture, kitty())));
            rows = await done.Task.ConfigureAwait(false);
        }

        rows ??= HalfBlockRows(picture);
        Remember(key, rows);
        return rows;
    }

    /// <summary>
    /// Decodes and sizes a picture. The pixels kept are the most either presentation needs: under
    /// Kitty the terminal scales into the box, so anything past <see cref="InlineImageLayout.CellPixelWidth"/>
    /// per column is bytes on the wire for nothing.
    /// </summary>
    internal static DecodedPicture? Decode(byte[] bytes, InlineImageRequest request, int availableColumns)
    {
        PixelBuffer source;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            source = PixelBuffer.FromStream(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }

        var box = InlineImageLayout.Fit(source.Width, source.Height, request.Width, request.Height, availableColumns);
        if (box.Columns <= 0 || box.Rows <= 0)
        {
            return null;
        }

        var width = Math.Min(source.Width, box.Columns * InlineImageLayout.CellPixelWidth);
        var height = Math.Min(source.Height, box.Rows * InlineImageLayout.CellPixelHeight);
        try
        {
            var scaled = width == source.Width && height == source.Height ? source : source.Resize(width, height);
            return new DecodedPicture(box, scaled);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Half-block rows for a decoded picture: one pixel across and two down per cell.</summary>
    internal static IReadOnlyList<string> HalfBlockRows(DecodedPicture picture)
    {
        var cells = picture.Pixels.Resize(picture.Box.Columns, picture.Box.Rows * 2);
        return InlineImageRows.HalfBlock(
            (x, y) =>
            {
                var p = cells.GetPixel(x, y);
                return (p.R, p.G, p.B);
            },
            picture.Box.Columns,
            picture.Box.Rows);
    }

    /// <summary>
    /// Sends the picture under a fresh id and returns the placeholder rows for it, or null when this
    /// driver cannot take it — in which case the caller falls back to half-blocks, which every
    /// truecolour terminal can show.
    /// </summary>
    private IReadOnlyList<string>? Transmit(DecodedPicture picture, IGraphicsProtocol? protocol)
    {
        if (protocol is not { SupportsKittyGraphics: true })
        {
            return null;
        }

        var limit = KittyGraphicsProtocol.RowColumnDiacritics.Length;
        var columns = Math.Min(picture.Box.Columns, limit);
        var rows = Math.Min(picture.Box.Rows, limit);
        var id = Interlocked.Increment(ref _nextKittyId) - 1;
        if (id > 0xFFFFFF)
        {
            return null;
        }

        try
        {
            protocol.TransmitRawRgb((uint)id, Rgb(picture.Pixels), picture.Pixels.Width, picture.Pixels.Height, columns, rows);
        }
        catch (NotSupportedException)
        {
            return null;
        }

        return InlineImageRows.Kitty(id, columns, rows);
    }

    private static byte[] Rgb(PixelBuffer pixels)
    {
        var data = new byte[pixels.Width * pixels.Height * 3];
        var i = 0;
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                var p = pixels.GetPixel(x, y);
                data[i++] = p.R;
                data[i++] = p.G;
                data[i++] = p.B;
            }
        }

        return data;
    }

    private IReadOnlyList<string>? Cached(string key)
    {
        lock (_gate)
        {
            for (var node = _cache.First; node is not null; node = node.Next)
            {
                if (node.Value.Key == key)
                {
                    _cache.Remove(node);
                    _cache.AddFirst(node);
                    return node.Value.Rows;
                }
            }
        }

        return null;
    }

    private void Remember(string key, IReadOnlyList<string> rows)
    {
        lock (_gate)
        {
            _cache.AddFirst((key, rows));
            while (_cache.Count > CacheSize)
            {
                _cache.RemoveLast();
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _fetches.Dispose();
    }
}

/// <summary>A decoded picture and the cell box it will fill.</summary>
internal sealed record DecodedPicture(WebImageLayout.CellBox Box, PixelBuffer Pixels);
