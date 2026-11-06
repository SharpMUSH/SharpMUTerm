namespace SharpMUTerm.Client.Modals;

/// <summary>
/// What a quit would end, as of the keystroke that asked — the quit prompt's model, drawn by the cell prompt
/// (<c>QuitPrompt.Render</c>) and by the window's native one. The client gathers it (only the client can see sessions,
/// windows and drafts); both prompts only say it.
/// </summary>
/// <param name="ConnectedCharacters">
/// The connections a quit drops, by <c>world.character</c> (a world's own name for an anonymous one) — the same list
/// and the same unit the header's fraction counts, from the client's single derivation.
/// </param>
/// <param name="Drafts">
/// How many unsent lines there are, counted per command line rather than per window, plus the posts the composer holds.
/// </param>
/// <param name="DraftWindows">The windows those drafts are sitting in, by title.</param>
/// <param name="RestartVersion">
/// Set when the quit is a restart into a downloaded update: the version it installs. The prompt then asks to restart,
/// and its yes button says Restart.
/// </param>
/// <param name="RestartNote">What restarting does in this host beyond the obvious, when there is anything (the host's words).</param>
public sealed record QuitModel(
    IReadOnlyList<string> ConnectedCharacters,
    int Drafts,
    IReadOnlyList<string> DraftWindows,
    string? RestartVersion = null,
    string? RestartNote = null)
{
    /// <summary>How many names a consequence line spells out before it starts counting instead.</summary>
    private const int MaxNames = 3;

    /// <summary>A client holding nothing: nothing connected and nothing typed.</summary>
    public static QuitModel Nothing { get; } = new([], 0, []);

    /// <summary>What the prompt says when quitting would cost nothing.</summary>
    public const string NothingAtStake = "Nothing is connected and nothing is unsent.";

    /// <summary>The question: quit, or restart into the update.</summary>
    public string Heading => RestartVersion is { } version ? $"Restart to update to SharpMUTerm {version}?" : "Quit SharpMUTerm?";

    /// <summary>What the yes button is called.</summary>
    public string Yes => RestartVersion is null ? "Quit" : "Restart";

    /// <summary>
    /// What quitting now would cost, one plain sentence per fact, and empty when it would cost nothing. A bare "are
    /// you sure?" is a ritual; this client knows what it is holding, so the question names it.
    /// </summary>
    public IReadOnlyList<string> Consequences
    {
        get
        {
            var lines = new List<string>();
            if (ConnectedCharacters.Count > 0)
            {
                var n = ConnectedCharacters.Count;
                lines.Add($"{n} character{Plural(n)} connected — {Names(ConnectedCharacters)}");
            }

            if (Drafts > 0)
            {
                var where = DraftWindows.Count > 0 ? $" — {Names(DraftWindows)}" : string.Empty;
                lines.Add($"{Drafts} unsent draft{Plural(Drafts)}{where}");
            }

            return lines;
        }
    }

    public bool Equals(QuitModel? other) =>
        other is not null
        && Drafts == other.Drafts
        && RestartVersion == other.RestartVersion
        && RestartNote == other.RestartNote
        && ModelLists.Same(ConnectedCharacters, other.ConnectedCharacters)
        && ModelLists.Same(DraftWindows, other.DraftWindows);

    public override int GetHashCode() =>
        HashCode.Combine(Drafts, ModelLists.Hash(ConnectedCharacters), ModelLists.Hash(DraftWindows), RestartVersion, RestartNote);

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    /// <summary>Names, capped: past a few they stop identifying anything and only cost the line's width.</summary>
    private static string Names(IReadOnlyList<string> names) =>
        names.Count <= MaxNames
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(MaxNames))} +{names.Count - MaxNames} more";
}

/// <summary>
/// One opening of the quit prompt in a host's own controls. <see cref="Answer"/> is <c>y</c> or <c>n</c>: quitting
/// ends in the client's own quit, staying closes it — the cell prompt's two outcomes. The default is Stay, as there.
/// </summary>
public sealed class QuitSession : ModalSession<QuitModel>
{
    private readonly Action _quit;

    /// <param name="model">What a quit would end.</param>
    /// <param name="isDark">Whether the client's theme is a dark one.</param>
    /// <param name="quit">Ends the client; called once, from any thread, after the prompt has closed.</param>
    /// <param name="closed">Told the prompt closed.</param>
    public QuitSession(QuitModel model, bool isDark, Action quit, Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(quit);
        _quit = quit;
    }

    public override string Surface => "quit";

    /// <summary>The quit prompt is in front of everything, whatever was open when it was asked for.</summary>
    public override bool AlwaysInFront => true;

    /// <summary>The host's: <c>y</c> quits (true) and <c>n</c> stays (false). Ignored once the prompt has closed.</summary>
    public void Answer(bool quit)
    {
        if (quit)
        {
            CloseThen(_quit);
        }
        else
        {
            Dismiss();
        }
    }
}
