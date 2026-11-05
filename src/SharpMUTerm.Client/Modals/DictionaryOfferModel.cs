namespace SharpMUTerm.Client.Modals;

/// <summary>
/// The spelling dictionary offer: what would be downloaded, from where, under what licence, and where it would go —
/// the consent text, drawn by the cell surface (<c>DictionaryOffer.Render</c>) and by the window's native modal. Nothing
/// is fetched until the player says yes, and the offer exists only because the player asked (F1's ⌥W with no
/// dictionary, ⌃P ▸ <em>Get a spelling dictionary</em>).
/// </summary>
/// <param name="Name">The dictionary's file name (<c>en_US</c>).</param>
/// <param name="Language">What a reader calls it (<c>English (US)</c>).</param>
/// <param name="Origin">Where it comes from.</param>
/// <param name="Licence">Its licence.</param>
/// <param name="Host">The host the files are fetched from.</param>
/// <param name="Size">About how big it is, in words (<c>about 0.6 MB</c>).</param>
/// <param name="Folder">Where it would be written, or null when this run keeps no dictionaries folder (a test, the demo).</param>
/// <param name="Installed">Whether that folder already holds a dictionary, which a download would sit beside.</param>
public sealed record DictionaryOfferModel(
    string Name,
    string Language,
    string Origin,
    string Licence,
    string Host,
    string Size,
    string? Folder,
    bool Installed)
{
    /// <summary>The offer's question.</summary>
    public string Question => $"Download the {Language} spelling dictionary?";

    /// <summary>What the offer says, one sentence per line.</summary>
    public IReadOnlyList<string> Lines =>
    [
        $"{Name} — {Origin}, {Size}, fetched over https from {Host} and checked against the file this client expects.",
        $"Licence: {Licence}.",
        Folder is { } folder
            ? $"It is saved in {folder}, and the composer checks spelling with it from then on."
            : "This run keeps no dictionaries folder, so nothing can be saved: the live client saves it beside your configuration.",
        Installed
            ? "That folder already has a dictionary; this one is added beside it."
            : "Nothing else is downloaded, and nothing is sent about you.",
    ];

    /// <summary>The keys the cell offer answers.</summary>
    public const string Keys = "⏎ download · Esc not now";
}

/// <summary>
/// One opening of the dictionary offer in a host's own controls. <see cref="Accept"/> closes it and starts the
/// download through the client's own method; Esc (or a click outside) closes it and fetches nothing.
/// </summary>
public sealed class DictionaryOfferSession : ModalSession<DictionaryOfferModel>
{
    private readonly Action _accept;

    public DictionaryOfferSession(DictionaryOfferModel model, bool isDark, Action accept, Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(accept);
        _accept = accept;
    }

    public override string Surface => "dictionary";

    /// <summary>The host's: yes, download it.</summary>
    public void Accept() => CloseThen(_accept);
}
