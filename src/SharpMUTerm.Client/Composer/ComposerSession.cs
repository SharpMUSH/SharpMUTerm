using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Client.Composer;

/// <summary>
/// What a native composer hands back to the client. Each is called from the host's own thread; the client marshals
/// onto its own and runs the path the cell composer's key runs (⌃S, ⌥L, ⌥W, Esc), so a gesture in either host ends in
/// the same client method. Any may be null where a composer has no such gesture (F1 is never released or armed).
/// </summary>
public sealed record ComposerCallbacks
{
    /// <summary>The writer changed the text: the whole buffer, lines broken by <c>\n</c>.</summary>
    public Action<string>? Edited { get; init; }

    /// <summary>⌃S, with the buffer as it stands.</summary>
    public Action<string>? Send { get; init; }

    /// <summary>⌥L.</summary>
    public Action? ToggleEscaping { get; init; }

    /// <summary>⌥P: F1's as-sent preview on or off. Null for a composer with none (the docked panel).</summary>
    public Action? TogglePreview { get; init; }

    /// <summary>⌥W, or the menu's <em>Add to dictionary</em>, with the word.</summary>
    public Action<string>? AddWord { get; init; }

    /// <summary>⌥W in F1 with no dictionary: the client offers to download one.</summary>
    public Action? GetDictionary { get; init; }

    /// <summary>Esc in a docked composer: the keyboard goes back to the command line.</summary>
    public Action? Release { get; init; }

    /// <summary>A click in a docked composer: it takes the keyboard.</summary>
    public Action? Arm { get; init; }

    /// <summary>Esc (or F1) in F1: the window closes, with the buffer to keep.</summary>
    public Action<string>? Closed { get; init; }

    /// <summary>
    /// Whether the composer's character writes in a proportional face — read by the host on its own thread, so it reads
    /// the character's saved choice live and every composer of that character follows a change. Null: monospace.
    /// </summary>
    public Func<bool>? Proportional { get; init; }

    /// <summary>The editor's menu chose a face (true for proportional), for the character to keep.</summary>
    public Action<bool>? ChooseFace { get; init; }
}

/// <summary>
/// One composer, for a host that draws it with its own editor: the model the client publishes, the dictionary to mark
/// and suggest from on the host's thread, and the way back. F1 gets a session per opening; a docked composer one per
/// panel window, for as long as the panel lives.
/// <para>
/// <b>The client publishes; the host draws.</b> <see cref="Publish"/> is the client's (on its thread), and raises
/// <see cref="Changed"/> only when the model differs, so a host can redraw on every one. Everything else is the host's.
/// Once the client has <see cref="End"/>ed a session (F1 closed by a send, the panel gone), nothing the host says is
/// passed on: a late Esc cannot close a composer that already closed.
/// </para>
/// </summary>
public sealed class ComposerSession
{
    private readonly Func<SpellChecker?>? _spelling;
    private readonly ComposerCallbacks _callbacks;
    private ComposerModel _model;
    private int _ended;

    /// <param name="model">The model to open on.</param>
    /// <param name="spelling">
    /// The dictionary the client marks with, read on the host's thread; it must not open one (a dictionary is opened by
    /// the client, which then says so in <see cref="ComposerModel.Dictionary"/>). Null for none.
    /// </param>
    /// <param name="callbacks">Where the host's gestures go.</param>
    public ComposerSession(ComposerModel model, Func<SpellChecker?>? spelling, ComposerCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(callbacks);
        _model = model;
        _spelling = spelling;
        _callbacks = callbacks;
    }

    /// <summary>The model last published.</summary>
    public ComposerModel Model => Volatile.Read(ref _model);

    /// <summary>Raised on the client's thread when a published model differs from the last; a host marshals.</summary>
    public event Action<ComposerModel>? Changed;

    /// <summary>Whether the client has ended the session.</summary>
    public bool IsEnded => Volatile.Read(ref _ended) != 0;

    /// <summary>The client's: a new model, passed on when it differs from the last.</summary>
    public void Publish(ComposerModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var old = Interlocked.Exchange(ref _model, model);
        if (!model.Equals(old))
        {
            Changed?.Invoke(model);
        }
    }

    /// <summary>The client's: nothing the host says from now on is passed on.</summary>
    public void End() => Volatile.Write(ref _ended, 1);

    /// <summary>
    /// The words to mark in <paramref name="body"/> — the host's text as it is now, which may be a keystroke ahead of
    /// <see cref="Model"/>. The same function the model's <see cref="ComposerModel.Marks"/> are made by, so for the
    /// same text the two agree. None for a MOO edit or with no dictionary.
    /// </summary>
    public IReadOnlyList<SpellRange> Marks(string body) =>
        Model is { Edit: null, Dictionary: not null } ? ComposerMarks.For(body ?? string.Empty, _spelling?.Invoke()) : [];

    /// <summary>The dictionary's suggestions for a marked word, for the host's menu; empty with none.</summary>
    public IReadOnlyList<string> Suggestions(string word) =>
        CanAddWords && _spelling?.Invoke() is { } checker ? checker.Suggest(word) : [];

    /// <summary>Whether ⌥W does anything here: a post marked by a dictionary.</summary>
    public bool CanAddWords => Model is { Edit: null, Dictionary: not null };

    /// <summary>The host's: the writer changed the text.</summary>
    public void Edited(string body) => Pass(_callbacks.Edited, Normalise(body));

    /// <summary>The host's: ⌃S.</summary>
    public void Send(string body) => Pass(_callbacks.Send, Normalise(body));

    /// <summary>The host's: ⌥L. Nothing for a MOO edit, which is saved as its lines.</summary>
    public void ToggleEscaping()
    {
        if (Model.Edit is null && !IsEnded)
        {
            _callbacks.ToggleEscaping?.Invoke();
        }
    }

    /// <summary>The host's: ⌥W on <paramref name="word"/>.</summary>
    /// <summary>⌥P: shows or hides the post as it will be sent. Nothing for a MOO edit, or a session the client ended.</summary>
    public void TogglePreview()
    {
        if (Model.Edit is null && !IsEnded)
        {
            _callbacks.TogglePreview?.Invoke();
        }
    }

    public void AddWord(string word)
    {
        if (CanAddWords && !string.IsNullOrWhiteSpace(word))
        {
            Pass(_callbacks.AddWord, word.Trim());
        }
    }

    /// <summary>Whether ⌥W here offers a dictionary: F1 writing a post, with none marking it.</summary>
    public bool CanGetDictionary => Model is { Edit: null, Dictionary: null, Place: ComposerPlace.FullScreen };

    /// <summary>The host's: ⌥W with no dictionary.</summary>
    public void GetDictionary()
    {
        if (CanGetDictionary && !IsEnded)
        {
            _callbacks.GetDictionary?.Invoke();
        }
    }

    /// <summary>The host's: Esc in a docked composer.</summary>
    public void Release()
    {
        if (!IsEnded)
        {
            _callbacks.Release?.Invoke();
        }
    }

    /// <summary>The host's: a click in a docked composer.</summary>
    public void Arm()
    {
        if (!IsEnded)
        {
            _callbacks.Arm?.Invoke();
        }
    }

    /// <summary>Whether this composer's character writes in a proportional face; false (monospace) with nobody to ask.</summary>
    public bool Proportional => _callbacks.Proportional?.Invoke() ?? false;

    /// <summary>The host's: the editor's menu chose a face.</summary>
    public void ChooseFace(bool proportional)
    {
        if (!IsEnded)
        {
            _callbacks.ChooseFace?.Invoke(proportional);
        }
    }

    /// <summary>The host's: F1 closing by Esc, with the buffer.</summary>
    public void Close(string body) => Pass(_callbacks.Closed, Normalise(body));

    private void Pass(Action<string>? action, string value)
    {
        if (!IsEnded)
        {
            action?.Invoke(value);
        }
    }

    /// <summary>Lines broken by <c>\n</c>: a native text box on Windows writes CRLF.</summary>
    private static string Normalise(string? body) => (body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
}
