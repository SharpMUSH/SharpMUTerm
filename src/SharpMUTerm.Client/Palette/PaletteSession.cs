using SharpMUTerm.Core.Commands;

namespace SharpMUTerm.Client.Palette;

/// <summary>
/// One opening of ⌃P, for a host that draws the palette with its own controls. Everything that depends on the
/// client's state is captured when it opens, on the client's thread — the catalog, the recent ids, the suggested
/// ones and whose session the entries act on — so a host can filter it on its own thread with nothing to lock.
/// <see cref="View"/> is <see cref="CommandPaletteModel.Build"/> over that capture, so a native palette lists the
/// same rows in the same order as the cell one for the same query.
/// <para>
/// Running an entry and closing go back through two delegates the client supplies; the client marshals them onto
/// its own thread and runs the entry through the cell palette's own path (<c>RunFromPalette</c>), so recents are
/// recorded exactly as they are there. Each session runs at most one entry and closes once.
/// </para>
/// </summary>
public sealed class PaletteSession
{
    private readonly IReadOnlyList<CommandItem> _catalog;
    private readonly IReadOnlyList<string> _recent;
    private readonly IReadOnlyList<string> _suggested;
    private readonly Action<string> _run;
    private readonly Action _closed;
    private int _ended;

    /// <param name="catalog">Every entry offered when the palette opened, in registration order.</param>
    /// <param name="recent">Ids run from the palette, newest first.</param>
    /// <param name="suggested">Ids the client's state makes likely (<see cref="CommandPaletteModel.Suggested"/>).</param>
    /// <param name="context">Whose session the entries act on, or null; the cell palette's <c>acting on</c>.</param>
    /// <param name="isDark">Whether the client's theme is a dark one, so the host can follow it.</param>
    /// <param name="initialQuery">What the palette opens with typed in; empty for nothing.</param>
    /// <param name="run">Runs an entry by id; called once, from any thread.</param>
    /// <param name="closed">Told the palette closed, whether or not an entry ran; called once, from any thread.</param>
    public PaletteSession(
        IReadOnlyList<CommandItem> catalog,
        IReadOnlyList<string> recent,
        IReadOnlyList<string> suggested,
        string? context,
        bool isDark,
        string initialQuery,
        Action<string> run,
        Action closed)
    {
        ArgumentNullException.ThrowIfNull(initialQuery);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(suggested);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(closed);
        _catalog = catalog.ToArray();
        _recent = recent.ToArray();
        _suggested = suggested.ToArray();
        _run = run;
        _closed = closed;
        Context = context;
        IsDark = isDark;
        InitialQuery = initialQuery;
    }

    /// <summary>What the palette opens with typed in its box; empty for nothing.</summary>
    public string InitialQuery { get; }

    /// <summary>Whose session the entries act on, or null.</summary>
    public string? Context { get; }

    /// <summary>Whether the client's theme is a dark one.</summary>
    public bool IsDark { get; }

    /// <summary>A copy of the client's theme when the palette opened, for the host's colours; null for the variant's built-in.</summary>
    public Core.Theming.Theme? Theme { get; init; }

    /// <summary>How many entries the catalog held when the palette opened.</summary>
    public int Total => _catalog.Count;

    /// <summary>Whether <see cref="Run"/> or <see cref="Dismiss"/> has been called.</summary>
    public bool IsEnded => Volatile.Read(ref _ended) != 0;

    /// <summary>The rows for <paramref name="query"/>: exactly what the cell palette lists for it.</summary>
    public PaletteView View(string query) =>
        CommandPaletteModel.Build(query ?? string.Empty, _catalog, _recent, _suggested);

    /// <summary>
    /// The line over the list, in the cell palette's words: <c>12 of 80   acting on Aetherfall.Corvid</c>.
    /// </summary>
    public string Status(PaletteView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var count = $"{view.Entries.Count} of {view.Total}";
        return string.IsNullOrEmpty(Context) ? count : $"{count}   acting on {Context}";
    }

    /// <summary>Closes the palette and runs <paramref name="id"/>. Ignored once the session has ended.</summary>
    public void Run(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (Interlocked.Exchange(ref _ended, 1) != 0)
        {
            return;
        }

        // Closed first, as the cell palette closes before it dispatches: the entry may open another surface.
        _closed();
        _run(id);
    }

    /// <summary>Closes the palette without running anything. Ignored once the session has ended.</summary>
    public void Dismiss()
    {
        if (Interlocked.Exchange(ref _ended, 1) == 0)
        {
            _closed();
        }
    }
}
