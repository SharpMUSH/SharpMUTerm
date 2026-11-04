namespace SharpMUTerm.Client.Palette;

/// <summary>
/// The commands most recently run from the command surface, newest first, at most <see cref="Capacity"/>.
/// Running one again moves it to the front rather than listing it twice.
/// <para>
/// Held in memory for the life of the run and never written down. The nearest thing this client keeps is the
/// command line's history, which is in memory too (<c>SharpMUTermApp._histories</c>); the only view state that
/// outlives a run goes into <c>config.json</c> through <c>PersistConfiguration</c>, and "which menu entries I
/// clicked" is not configuration. A file of its own would be one more thing this client writes, purges and
/// documents, for a list a person rebuilds in eight clicks.
/// </para>
/// </summary>
public sealed class RecentCommands
{
    /// <summary>How many the surface shows by default.</summary>
    public const int DefaultCapacity = 8;

    private readonly List<string> _ids = new();

    /// <summary>Creates an empty list holding at most <paramref name="capacity"/> ids.</summary>
    public RecentCommands(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    /// <summary>The most ids held.</summary>
    public int Capacity { get; }

    /// <summary>The ids, newest first.</summary>
    public IReadOnlyList<string> Ids => _ids;

    /// <summary>Puts <paramref name="commandId"/> at the front, dropping the oldest past <see cref="Capacity"/>.</summary>
    public void Record(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        _ids.Remove(commandId);
        _ids.Insert(0, commandId);
        if (_ids.Count > Capacity)
        {
            _ids.RemoveRange(Capacity, _ids.Count - Capacity);
        }
    }

    /// <summary>Forgets every id.</summary>
    public void Clear() => _ids.Clear();
}
