namespace SharpMUTerm.Client.Modals;

/// <summary>
/// One opening of a modal the client asks a host to draw with its own controls — the quit prompt, ⌃F search, ⌃R
/// history, the ⌥M layouts list, the first-run welcome. The client builds it on its own thread with everything the
/// modal says, and the host draws it and hands every gesture back through it; the client marshals each onto its own
/// thread and ends it in the method the cell modal's key ends in, so the host only draws.
/// <para>
/// A session ends once: when the host closes it (<see cref="Dismiss"/>, or a gesture that closes it) or when the
/// client does (<see cref="End"/>). After that nothing the host says is passed on, so a late Esc cannot close a modal
/// that already closed, and a second ⏎ cannot quit twice.
/// </para>
/// </summary>
public abstract class ModalSession
{
    private readonly Action _closed;
    private int _ended;

    /// <param name="isDark">Whether the client's theme is a dark one, so the host can follow it.</param>
    /// <param name="closed">Told the modal closed, however it did; called once, from any thread.</param>
    protected ModalSession(bool isDark, Action closed)
    {
        ArgumentNullException.ThrowIfNull(closed);
        IsDark = isDark;
        _closed = closed;
    }

    /// <summary>Whether the client's theme is a dark one.</summary>
    public bool IsDark { get; }

    /// <summary>The surface this is (<c>SurfaceCatalog</c>'s id).</summary>
    public abstract string Surface { get; }

    /// <summary>Whether the modal has closed.</summary>
    public bool IsEnded => Volatile.Read(ref _ended) != 0;

    /// <summary>
    /// Whether this modal must be in front of every other thing the host draws — the quit prompt, which can be asked
    /// for from anywhere and must never be answered blind.
    /// </summary>
    public virtual bool AlwaysInFront => false;

    /// <summary>The host's: Esc, or a click outside the modal. Closes it, running nothing.</summary>
    public void Dismiss()
    {
        if (TryEnd())
        {
            _closed();
        }
    }

    /// <summary>The client's: the modal has gone (the client closed it); nothing the host says is passed on.</summary>
    public void End() => Volatile.Write(ref _ended, 1);

    /// <summary>
    /// Ends the session and says it closed, then runs <paramref name="then"/> — closed first, as a cell modal closes
    /// before it acts, because what it runs may open another surface. False when it had already ended.
    /// </summary>
    protected bool CloseThen(Action then)
    {
        if (!TryEnd())
        {
            return false;
        }

        _closed();
        then();
        return true;
    }

    private bool TryEnd() => Interlocked.Exchange(ref _ended, 1) == 0;
}

/// <summary>
/// A modal session whose model the client republishes while it is open (search results as the query changes, a
/// layout list's status after an action, the welcome's refusal).
/// </summary>
/// <typeparam name="TModel">The Client model the cells draw and the host draws.</typeparam>
public abstract class ModalSession<TModel> : ModalSession
    where TModel : class
{
    private TModel _model;

    protected ModalSession(TModel model, bool isDark, Action closed)
        : base(isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
    }

    /// <summary>The model last published.</summary>
    public TModel Model => Volatile.Read(ref _model);

    /// <summary>Raised on the client's thread when a published model differs from the last; a host marshals.</summary>
    public event Action<TModel>? Changed;

    /// <summary>The client's: a new model, passed on when it differs from the last.</summary>
    public void Publish(TModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var old = Interlocked.Exchange(ref _model, model);
        if (!model.Equals(old))
        {
            Changed?.Invoke(model);
        }
    }
}

/// <summary>Value equality over the lists a model carries, which a record compares by reference.</summary>
internal static class ModelLists
{
    public static bool Same<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) =>
        ReferenceEquals(a, b) || (a.Count == b.Count && a.SequenceEqual(b));

    public static int Hash<T>(IReadOnlyList<T> list)
    {
        var hash = new HashCode();
        hash.Add(list.Count);
        foreach (var item in list.Take(16))
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
