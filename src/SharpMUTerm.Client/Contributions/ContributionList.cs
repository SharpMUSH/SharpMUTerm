using System.Collections;

namespace SharpMUTerm.Client.Contributions;

/// <summary>
/// One kind's contributions, in registration order. Registration order is the order every reader sees unless
/// the kind defines its own (a settings section's <c>Order</c>, a status cell's <c>DropOrder</c>), and ties
/// there are broken by registration order too, so the same registrations always read back the same way.
/// <para>
/// Registered and read on the client's UI thread. It is not a concurrent collection.
/// </para>
/// </summary>
/// <typeparam name="T">The contribution type.</typeparam>
public class ContributionList<T> : IReadOnlyList<T>
    where T : class, IContribution
{
    private readonly List<T> _items = new();
    private readonly Dictionary<string, T> _byId = new(StringComparer.Ordinal);

    internal ContributionList(ContributionKind kind) => Kind = kind;

    /// <summary>The kind this list holds.</summary>
    public ContributionKind Kind { get; }

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public T this[int index] => _items[index];

    /// <summary>
    /// Adds a contribution after every one already registered.
    /// </summary>
    /// <exception cref="ArgumentException">The id is empty, or another contribution of this kind already has it.</exception>
    public void Add(T contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentException.ThrowIfNullOrWhiteSpace(contribution.Id);
        ArgumentNullException.ThrowIfNull(contribution.Owner);
        if (_byId.TryGetValue(contribution.Id, out var existing))
        {
            throw new ArgumentException(
                $"a {Kind} with the id '{contribution.Id}' is already registered by {existing.Owner}",
                nameof(contribution));
        }

        Validate(contribution);
        _items.Add(contribution);
        _byId.Add(contribution.Id, contribution);
    }

    /// <summary>The contribution with this id, or null.</summary>
    public T? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Whether a contribution has this id.</summary>
    public bool Contains(string id) => _byId.ContainsKey(id);

    /// <summary>Removes every contribution this owner registered, keeping the order of the rest. Returns how many went.</summary>
    public int RemoveOwnedBy(ContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var removed = _items.RemoveAll(c => c.Owner == owner);
        if (removed > 0)
        {
            foreach (var id in _byId.Where(p => p.Value.Owner == owner).Select(p => p.Key).ToList())
            {
                _byId.Remove(id);
            }
        }

        return removed;
    }

    /// <summary>A further check a kind makes before accepting a contribution; throw to refuse it.</summary>
    protected virtual void Validate(T contribution)
    {
    }

    /// <summary>The contributions sorted by <paramref name="key"/>, ties left in registration order.</summary>
    protected IReadOnlyList<T> SortedBy(Func<T, int> key) =>
        _items.Select((item, index) => (item, index))
            .OrderBy(p => key(p.item))
            .ThenBy(p => p.index)
            .Select(p => p.item)
            .ToList();

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
