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
/// so a server spamming tags gets placeholders rather than a client with a hundred sockets open.
/// </para>
/// <para>
/// Two caches, because a picture is used at more than one size. The decoded picture is kept per URL
/// (<see cref="SourceCacheBytes"/>), and a fetch already running is shared, so the room picture a game
/// re-sends on every <c>look</c> is fetched and decoded once for the session, whatever width the pane is
/// when it arrives and however many looks land before the first fetch is back. Decoding is the costly
/// half: a WebP the size of a desktop background is tens of milliseconds of SIMD work each time. The rows
/// are kept as well, per URL, presentation and box, so a repeat at the same size is not even re-scaled
/// and, under Kitty, not re-transmitted — later copies are placeholder cells pointing at the image the
/// terminal already holds.
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
    /// Decoded pictures kept for reuse, in bytes of pixels. A 1942×809 room picture is about 6 MB, so this
    /// holds a session's worth of rooms; the least recently used goes first, and one larger than the whole
    /// budget is used once and not kept.
    /// </summary>
    public const long SourceCacheBytes = 64L * 1024 * 1024;

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
    private readonly Dictionary<string, LinkedListNode<Source>> _sources = new(StringComparer.Ordinal);
    private readonly LinkedList<Source> _sourceOrder = new();
    private readonly long _sourceBudget;
    private long _sourceBytes;
    private readonly object _gate = new();
    private int _queued;
    private int _nextKittyId = FirstKittyId;

    /// <param name="fetch">Fetches an image's bytes, or null.</param>
    /// <param name="onUi">Marshals onto the UI thread.</param>
    /// <param name="sourceCacheBytes">The decoded-picture budget; <see cref="SourceCacheBytes"/> unless a test says otherwise.</param>
    public InlineImageHost(
        Func<string, CancellationToken, Task<byte[]?>> fetch,
        Action<Action> onUi,
        long sourceCacheBytes = SourceCacheBytes)
    {
        _sourceBudget = sourceCacheBytes;
        _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
        _onUi = onUi ?? throw new ArgumentNullException(nameof(onUi));
    }

    private readonly HashSet<Task> _running = new();

    /// <summary>
    /// Told, on the UI thread, about each picture drawn as half-blocks: the cells its rows were made from
    /// and the picture at its best. <see cref="SixelOverlay"/> listens, to draw the picture again over them.
    /// A result served from the cache is not reported again.
    /// </summary>
    public Action<PixelBuffer, PixelBuffer>? HalfBlockDrawn { get; set; }

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
    /// on the UI thread, with its index in <paramref name="requests"/>: a picture that fails delivers
    /// nothing, so the order alone does not say which one arrived. <paramref name="kitty"/> is asked on the UI thread, at delivery: it is the
    /// driver, and the transmit has to happen just before the rows that point at it are painted.
    /// </summary>
    /// <returns>False when the queue is full and nothing was started.</returns>
    public bool Load(
        IReadOnlyList<InlineImageRequest> requests,
        InlineImagePresentation presentation,
        int availableColumns,
        Func<IGraphicsProtocol?> kitty,
        Action<int, IReadOnlyList<string>> deliver,
        int maxRows = InlineImageLayout.MaxRows)
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
                for (var i = 0; i < requests.Count; i++)
                {
                    var index = i;
                    var rows = await RowsForAsync(requests[index], presentation, availableColumns, maxRows, kitty).ConfigureAwait(false);
                    if (rows is not null && !_cts.IsCancellationRequested)
                    {
                        _onUi(() => deliver(index, rows));
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
        int maxRows,
        Func<IGraphicsProtocol?> kitty)
    {
        var key = $"{presentation}|{availableColumns}|{maxRows}|{request.Width}|{request.Height}|{request.Url}";
        if (Cached(key) is { } hit)
        {
            return hit;
        }

        var source = await SourceFor(request.Url).ConfigureAwait(false);
        var picture = source is null ? null : Fit(source, request, availableColumns, maxRows);
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

        if (rows is null)
        {
            var cells = HalfBlockCells(picture);
            rows = HalfBlockRows(picture.Box, cells);
            if (HalfBlockDrawn is { } drawn)
            {
                _onUi(() => drawn(cells, picture.Pixels));
            }
        }

        Remember(key, rows);
        return rows;
    }

    /// <summary>
    /// The decoded picture at <paramref name="url"/>: from the cache, from a fetch already under way, or
    /// fetched and decoded now. Null when it cannot be had, and a failure is not remembered, so the next
    /// mention tries again.
    /// </summary>
    private Task<PixelBuffer?> SourceFor(string url)
    {
        lock (_gate)
        {
            if (_sources.TryGetValue(url, out var node))
            {
                _sourceOrder.Remove(node);
                _sourceOrder.AddFirst(node);
                return node.Value.Picture;
            }

            // In the cache before it starts, so a fetch that finishes at once still finds itself there to
            // settle; and off this thread, so a fetch that completes at once does not decode under the lock.
            var source = new Source(url);
            _sources[url] = _sourceOrder.AddFirst(source);
            source.Picture = Task.Run(async () =>
            {
                PixelBuffer? picture = null;
                try
                {
                    picture = await FetchAndDecodeAsync(url).ConfigureAwait(false);
                    return picture;
                }
                finally
                {
                    Settle(source, picture);
                }
            });
            return source.Picture;
        }
    }

    private async Task<PixelBuffer?> FetchAndDecodeAsync(string url)
    {
        byte[]? bytes;
        await _fetches.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            bytes = await _fetch(url, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _fetches.Release();
        }

        if (bytes is null)
        {
            return null;
        }

        try
        {
            return ImageDecoder.Decode(bytes);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Accounts for a finished fetch: forgets a failure, and trims the cache to its budget.</summary>
    private void Settle(Source source, PixelBuffer? picture)
    {
        lock (_gate)
        {
            if (!_sources.TryGetValue(source.Url, out var node) || !ReferenceEquals(node.Value, source))
            {
                return;
            }

            if (picture is null)
            {
                Forget(node);
                return;
            }

            source.Bytes = (long)picture.Width * picture.Height * 4;
            _sourceBytes += source.Bytes;
            for (var last = _sourceOrder.Last; _sourceBytes > _sourceBudget && last is not null;)
            {
                var previous = last.Previous;
                if (last.Value.Bytes > 0)
                {
                    Forget(last);
                }

                last = previous;
            }
        }
    }

    private void Forget(LinkedListNode<Source> node)
    {
        _sourceOrder.Remove(node);
        _sources.Remove(node.Value.Url);
        _sourceBytes -= node.Value.Bytes;
    }

    /// <summary>Decoded pictures held, and fetches under way — for tests.</summary>
    internal int SourcesHeld
    {
        get
        {
            lock (_gate)
            {
                return _sources.Count;
            }
        }
    }

    /// <summary>
    /// Decodes and sizes a picture. The pixels kept are the most either presentation needs: under
    /// Kitty the terminal scales into the box, so anything past <see cref="InlineImageLayout.CellPixelWidth"/>
    /// per column is bytes on the wire for nothing.
    /// </summary>
    internal static DecodedPicture? Decode(
        byte[] bytes,
        InlineImageRequest request,
        int availableColumns,
        int maxRows = InlineImageLayout.MaxRows)
    {
        PixelBuffer source;
        try
        {
            source = ImageDecoder.Decode(bytes);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }

        return Fit(source, request, availableColumns, maxRows);
    }

    /// <summary>Sizes a decoded picture to its cell box, keeping the pixels either presentation needs.</summary>
    internal static DecodedPicture? Fit(
        PixelBuffer source,
        InlineImageRequest request,
        int availableColumns,
        int maxRows = InlineImageLayout.MaxRows)
    {
        var box = InlineImageLayout.Fit(source.Width, source.Height, request.Width, request.Height, availableColumns, maxRows);
        if (box.Columns <= 0 || box.Rows <= 0)
        {
            return null;
        }

        var width = Math.Min(source.Width, box.Columns * InlineImageLayout.CellPixelWidth);
        var height = Math.Min(source.Height, box.Rows * InlineImageLayout.CellPixelHeight);
        try
        {
            var scaled = PixelScale.Stretch(source, width, height);
            return new DecodedPicture(box, scaled);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Half-block rows for a decoded picture: one pixel across and two down per cell.</summary>
    internal static IReadOnlyList<string> HalfBlockRows(DecodedPicture picture) =>
        HalfBlockRows(picture.Box, HalfBlockCells(picture));

    /// <summary>The picture at one pixel across and two down per cell — what its half-block rows are made of.</summary>
    internal static PixelBuffer HalfBlockCells(DecodedPicture picture) =>
        picture.Pixels.Resize(picture.Box.Columns, picture.Box.Rows * 2);

    private static IReadOnlyList<string> HalfBlockRows(WebImageLayout.CellBox box, PixelBuffer cells)
    {
        return InlineImageRows.HalfBlock(
            (x, y) =>
            {
                var p = cells.GetPixel(x, y);
                return (p.R, p.G, p.B);
            },
            box.Columns,
            box.Rows);
    }

    /// <summary>
    /// Sends the picture under a fresh id and returns the placeholder rows for it, or null when there is
    /// no driver to take it — in which case the caller falls back to half-blocks, which every
    /// truecolour terminal can show. Whether the terminal speaks the protocol is the caller's decision
    /// (<see cref="GraphicsSurface.CanPlaceKitty"/>), not the driver's flag: that flag is never set on
    /// Windows, and reading it here would undo a forced <c>SHARPMUTERM_GRAPHICS=kitty</c>.
    /// </summary>
    private IReadOnlyList<string>? Transmit(DecodedPicture picture, IGraphicsProtocol? protocol)
    {
        if (protocol is null)
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

    /// <summary>A decoded picture held by the cache, or the fetch that will produce it.</summary>
    private sealed class Source(string url)
    {
        public string Url { get; } = url;

        public Task<PixelBuffer?> Picture { get; set; } = Task.FromResult<PixelBuffer?>(null);

        /// <summary>Its size in the cache's budget; zero while the fetch is under way.</summary>
        public long Bytes { get; set; }
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
