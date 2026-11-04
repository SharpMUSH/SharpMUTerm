using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Packages;

namespace SharpMUTerm.Client.Packages;

/// <summary>
/// What the client does when a host's package page asks: the same methods the cell F10 keys and the cell browse and
/// review call, so there is one way to switch a package, one way to remove it and one consent path for an install.
/// Every call arrives on the client's thread.
/// </summary>
public interface IPackageHost
{
    /// <summary>Switches a package on or off, and saves.</summary>
    void SetEnabled(string id, bool on);

    /// <summary>Removes a package and its files, and saves.</summary>
    void Remove(string id);

    /// <summary>Copies a package's sets into the player's own, and saves.</summary>
    void CopySets(string id);

    /// <summary>Fetches the package index — the only time it is fetched — and publishes it.</summary>
    void Browse();

    /// <summary>Fetches the archive an index entry names, checks it against the entry and publishes its review.</summary>
    void Install(PackageIndexEntry entry);

    /// <summary>Installs what <paramref name="review"/> described, if it is still the review waiting.</summary>
    void Accept(PackageReviewModel review);

    /// <summary>Drops <paramref name="review"/> without writing anything.</summary>
    void Cancel(PackageReviewModel review);
}

/// <summary>
/// F10 as a host's native page reads it: the declared packages over the live configuration, the index once the player
/// has asked for it, the review waiting for consent and the last thing said. The client publishes into it on its own
/// thread; a host reads it on its own, and every value it reads is a model replaced whole, never edited. Every gesture
/// goes back through <see cref="IPackageHost"/>, so the window never installs, removes or switches anything itself.
/// </summary>
public sealed class PackageManager
{
    private readonly AppConfiguration _config;
    private readonly IPackageHost _host;

    /// <param name="config">The live configuration (its installed packages and which are off).</param>
    /// <param name="declaration">F10's declaration.</param>
    /// <param name="host">The client's side.</param>
    public PackageManager(AppConfiguration config, PackageCollection declaration, IPackageHost host)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(host);
        _config = config;
        Declaration = declaration;
        _host = host;
    }

    /// <summary>F10's declaration: the fields and actions both hosts draw.</summary>
    public PackageCollection Declaration { get; }

    /// <summary>The live configuration, for a binding to read through.</summary>
    public AppConfiguration Configuration => _config;

    /// <summary>The installed packages now, each with what the index said of it.</summary>
    public IReadOnlyList<PackageItem> Items => PackageCollection.Items(_config, Index);

    /// <summary>The index, once fetched this run; null before the player first browses.</summary>
    public PackageIndex? Index { get; private set; }

    /// <summary>The index as the browse list shows it, or null before it has arrived.</summary>
    public PackageBrowseModel? BrowseModel { get; private set; }

    /// <summary>
    /// How many times the player has asked for the index (here or by ⌃P ▸ Browse packages). A host compares it with
    /// <see cref=BrowseRequestsSeen/> to bring its browse list forward once per ask.
    /// </summary>
    public int BrowseRequests { get; private set; }

    /// <summary>The <see cref=BrowseRequests/> a host has answered by showing its browse list; the host's to write.</summary>
    public int BrowseRequestsSeen { get; set; }

    /// <summary>Whether the index is being fetched.</summary>
    public bool Fetching { get; private set; }

    /// <summary>The review waiting for consent, or null.</summary>
    public PackageReviewModel? Review { get; private set; }

    /// <summary>The last thing the client said about packages (installed, removed, refused), or null.</summary>
    public string? Status { get; private set; }

    /// <summary>Raised on the client's thread after anything here has changed.</summary>
    public event Action? Changed;

    /// <summary>Whether the package <paramref name="item"/> is on.</summary>
    public bool IsEnabled(PackageItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return PackageCollection.IsEnabled(_config, item.Id);
    }

    /// <summary>What <paramref name="field"/> says for <paramref name="item"/>.</summary>
    public string Text(InfoField field, PackageItem item)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Read(new SettingTarget(_config, Item: item));
    }

    // ── gestures, handed to the client ───────────────────────────────────────────────────────────

    /// <summary>Switches <paramref name="item"/> on or off.</summary>
    public void SetEnabled(PackageItem item, bool on)
    {
        ArgumentNullException.ThrowIfNull(item);
        _host.SetEnabled(item.Id, on);
    }

    /// <summary>Removes <paramref name="item"/>.</summary>
    public void Remove(PackageItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _host.Remove(item.Id);
    }

    /// <summary>Copies <paramref name="item"/>'s sets into the player's own.</summary>
    public void CopySets(PackageItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _host.CopySets(item.Id);
    }

    /// <summary>Fetches the index. Nothing fetches it before this is called.</summary>
    public void Browse() => _host.Browse();

    /// <summary>Fetches and checks the package <paramref name="row"/> names, and opens its review.</summary>
    public void Install(PackageBrowseRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _host.Install(row.Entry);
    }

    /// <summary>Installs the package under review: the consent.</summary>
    public void Accept()
    {
        if (Review is { } review)
        {
            _host.Accept(review);
        }
    }

    /// <summary>Drops the review; nothing is written.</summary>
    public void Cancel()
    {
        if (Review is { } review)
        {
            _host.Cancel(review);
        }
    }

    // ── what the client publishes, on its own thread ─────────────────────────────────────────────

    /// <summary>Says the index is on its way.</summary>
    public void PublishFetching()
    {
        Fetching = true;
        BrowseRequests++;
        Changed?.Invoke();
    }

    /// <summary>Publishes the index and the browse list built from it.</summary>
    public void PublishIndex(PackageIndex index, PackageBrowseModel model)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(model);
        Index = index;
        BrowseModel = model;
        Fetching = false;
        Changed?.Invoke();
    }

    /// <summary>Publishes a review waiting for consent, or null for none.</summary>
    public void PublishReview(PackageReviewModel? review)
    {
        Review = review;
        Changed?.Invoke();
    }

    /// <summary>Publishes what the client said.</summary>
    public void PublishStatus(string? status)
    {
        Status = status;
        Fetching = false;
        Changed?.Invoke();
    }

    /// <summary>Raises <see cref="Changed"/>: the installed list or which are on moved.</summary>
    public void NotifyChanged() => Changed?.Invoke();
}
