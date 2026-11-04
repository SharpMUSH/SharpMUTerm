using SharpMUTerm.Client.Composer;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Telnet;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Tui.Panels;

namespace SharpMUTerm.Tui;

/// <summary>
/// The composer in a host that draws it with its own editor (<see cref="ClientHost.NativeComposer"/>), and the model
/// both hosts draw it from (<see cref="ComposerModel"/>).
/// <para>
/// <b>F1</b> opens there instead of the cell modal, through one funnel (<see cref="OpenComposer"/>) that every way in
/// takes — the key, ⌃P, a MOO edit arriving, the demo views. It is closed through one funnel too
/// (<see cref="CloseComposer"/>), which keeps the draft exactly as the cell window's <c>Closed</c> does, so the
/// send path's "close first, forget after" holds for both.
/// </para>
/// <para>
/// <b>A docked composer keeps its cells</b>, and the host's editor lies over them. The cell editor stays the draft:
/// the native editor's text is written into it as it changes (<see cref="DockedComposer.SetFromHost"/>), and the model
/// handed back carries a revision that moves only when something else changed the buffer. Arming stays the client's
/// one fact: a click in the native editor asks to arm, Esc in it releases, and the model says which it is.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>F1 drawn by the host: the session and the draft it holds, or null while none is open.</summary>
    private NativeCompose? _nativeCompose;

    /// <summary>The native editor's channel for each docked composer, by window id; one per panel window.</summary>
    private readonly Dictionary<string, ComposerSession> _dockedSessions = new(StringComparer.Ordinal);

    /// <summary>The dictionary, once opened: by the first F1, or the first time a docked composer is armed.</summary>
    private SpellChecker? _composerSpelling;

    private bool _composerSpellingOpened;

    /// <summary>The host's composer, when it has somewhere to draw one; null for cells.</summary>
    private INativeComposer? NativeComposerHost => Host.NativeComposer is { Available: true } native ? native : null;

    /// <summary>Whether F1 is open, in cells or natively.</summary>
    internal bool ComposerOpen => _composer.IsOpen || _nativeCompose is not null;

    /// <summary>Whether F1 is open in the host's own editor, for the tests.</summary>
    internal bool NativeComposerIsOpen => _nativeCompose is not null;

    /// <summary>The native F1's session, for the tests; null while none is open.</summary>
    internal ComposerSession? NativeComposerSession => _nativeCompose?.Session;

    /// <summary>The buffer F1 holds now, in either host.</summary>
    private string ComposerBody => _nativeCompose?.Body ?? _composer.Body;

    /// <summary>
    /// The composer's dictionary, opened the first time anything asks — never at startup, so a client nobody writes a
    /// post in reads none.
    /// </summary>
    private SpellChecker? ComposerSpelling()
    {
        if (!_composerSpellingOpened)
        {
            _composerSpellingOpened = true;
            _composerSpelling = _spelling?.Invoke();
        }

        _composerSpelling?.KnowNames(SpellingNames());
        return _composerSpelling;
    }

    /// <summary>
    /// The proper names the composer never marks: every configured world and character, and the names each open
    /// session's game has reported (<see cref="Core.Commands.CompletionIndex.Names"/>). A post that names its writer or
    /// the people in the room is not misspelt.
    /// </summary>
    private IEnumerable<string> SpellingNames()
    {
        foreach (var world in _config.Worlds)
        {
            yield return world.Name;
            foreach (var character in world.Characters)
            {
                yield return character.Name;
            }
        }

        foreach (var session in _sessions.Sessions)
        {
            foreach (var name in session.Completions.Names())
            {
                yield return name;
            }
        }
    }

    /// <summary>
    /// Opens F1 on a draft: in the host's editor when it has one, else the cell window. Both are given the same draft
    /// and say the same thing about it (<see cref="ComposeOverlay.ModelFor"/>).
    /// </summary>
    private void OpenComposer(string target, string draft, ComposeEscaping escaping, bool canSend, MooEdit? edit = null, ScriptEdit? script = null)
    {
        // The names a game reports change between openings; the dictionary learns them each time F1 opens.
        if (_composerSpellingOpened)
        {
            ComposerSpelling();
        }

        if (NativeComposerHost is not { } native)
        {
            _composer.Toggle(target, draft, escaping, canSend, edit, script);
            return;
        }

        var open = new NativeCompose(target, canSend, script is null ? edit : null)
        {
            Body = draft.Replace("\r\n", "\n", StringComparison.Ordinal),
            Escaping = escaping,
            Script = script,
        };
        open.Session = new ComposerSession(
            NativeComposeModel(open),
            () => _composerSpelling,
            new ComposerCallbacks
            {
                Edited = body => OnUiThread(() =>
                {
                    if (ReferenceEquals(_nativeCompose, open))
                    {
                        // The model follows the text, so the marks it carries are the marks the cell window would draw.
                        open.Body = body;
                        open.Session.Publish(NativeComposeModel(open));
                    }
                }),
                Send = body => OnUiThread(() =>
                {
                    if (ReferenceEquals(_nativeCompose, open))
                    {
                        open.Body = body;
                        SendComposed(new ComposeResult(body, open.Escaping, open.Edit, open.Script));
                    }
                }),
                ToggleEscaping = () => OnUiThread(() =>
                {
                    if (ReferenceEquals(_nativeCompose, open) && open.Edit is null && open.Script is null)
                    {
                        open.Escaping = open.Escaping == ComposeEscaping.Literal ? ComposeEscaping.AsTyped : ComposeEscaping.Literal;
                        open.Session.Publish(NativeComposeModel(open));
                    }
                }),
                TogglePreview = () => OnUiThread(() =>
                {
                    if (ReferenceEquals(_nativeCompose, open) && open.Edit is null && open.Script is null)
                    {
                        // The cell window's fact, so F1 opens the same way in either host for the rest of the run.
                        _composer.Preview = !_composer.Preview;
                        open.Session.Publish(NativeComposeModel(open));
                    }
                }),
                AddWord = word => OnUiThread(() => AddComposerWord(word)),
                GetDictionary = () => OnUiThread(OfferDictionary),
                // A script is code, and code is read in monospace whatever face the character writes posts in.
                Proportional = script is null ? ComposerFaceOf(ComposeDraftKey(ActiveWindowId())) : () => false,
                ChooseFace = ChooseComposerFace(ComposeDraftKey(ActiveWindowId())),
                Closed = body => OnUiThread(() =>
                {
                    if (ReferenceEquals(_nativeCompose, open))
                    {
                        open.Body = body;
                        CloseComposer();
                    }
                }),
            });
        _nativeCompose = open;
        native.Open(open.Session, ComposerInk());
        PublishPanels();
        PublishHostChrome();
    }

    /// <summary>
    /// The face a character's native composer writes in, read on the host's thread: the character's saved choice
    /// (<see cref="CharacterDefinition.ComposerProportional"/>), looked up afresh each time so a change made in one of its
    /// composers reaches the other. A composer that belongs to nobody writes in monospace.
    /// </summary>
    private Func<bool> ComposerFaceOf(string? sessionKey)
    {
        var character = sessionKey is null ? null : CharacterFor(sessionKey);
        return () => character?.ComposerProportional ?? false;
    }

    /// <summary>
    /// Keeps a face chosen in a character's native composer as that character's, on disk. Through
    /// <see cref="PersistConfiguration"/> — a view preference, which must not go through <c>SaveConfiguration</c>, whose
    /// automation reload re-periodises every running timer.
    /// </summary>
    private Action<bool> ChooseComposerFace(string? sessionKey) => proportional => OnUiThread(() =>
    {
        if (sessionKey is not null && CharacterFor(sessionKey) is { } character && character.ComposerProportional != proportional)
        {
            character.ComposerProportional = proportional;
            PersistConfiguration();
        }
    });

    /// <summary>
    /// A host editor's colours: the accent its title and keys wear, and the settings screens' warning for the line under
    /// a misspelling — the colour the cell editor marks the word in.
    /// </summary>
    internal PanelInk ComposerInk() => PanelListInk() with { Low = _screens.Role(ScreenPalette.Warn) };

    /// <summary>The native F1's model for what it holds now; the cell window's for the same draft.</summary>
    private ComposerModel NativeComposeModel(NativeCompose open) =>
        ComposeOverlay.ModelFor(open.Target, open.Body, open.Escaping, open.Edit, open.Edit is null && open.Script is null ? ComposerSpelling() : null, open.CanSend, _composer.Preview, open.Script);

    /// <summary>
    /// Closes F1, in whichever host it is open, keeping its buffer as the character's draft (or MOO edit) exactly as the
    /// cell window's <c>Closed</c> does. A send closes through here first and forgets the draft after.
    /// </summary>
    private void CloseComposer()
    {
        if (_nativeCompose is not { } open)
        {
            _composer.Close();
            return;
        }

        _nativeCompose = null;
        open.Session.End();
        Host.NativeComposer?.Close();
        KeepComposeDraft(new ComposeResult(open.Body, open.Escaping, open.Edit, open.Script));
        PublishPanels();
        PublishHostChrome();
    }

    /// <summary>
    /// Adds a word to the dictionary from either composer, and re-marks everything a composer shows: the cell editors
    /// and the models handed to native editors.
    /// </summary>
    private void AddComposerWord(string word)
    {
        if (ComposerSpelling() is not { } checker || !checker.Add(word))
        {
            return;
        }

        foreach (var docked in _dockedComposers.Values)
        {
            docked.Editor.RefreshSyntaxHighlighting();
            docked.Redraw();
        }

        if (_nativeCompose is { } open)
        {
            open.Session.Publish(NativeComposeModel(open));
        }
    }

    // --- The docked composer's model and native channel ----------------------------------------------------------

    /// <summary>
    /// The docked composer's model: what its cells draw, recorded as drawn, and handed to its native editor. The target
    /// is the session's title, or the character's name while it has none (and then nothing can be sent).
    /// </summary>
    private ComposerModel DescribeDocked(DockedComposer composer)
    {
        var session = FindSession(composer.SessionKey);
        var target = session is not null
            ? SessionTitle(session)
            : FindCharacter(composer.SessionKey)?.Character.Name ?? string.Empty;
        var model = ComposerModel.Create(
            ComposerPlace.Docked,
            target,
            composer.Body,
            composer.Escaping,
            edit: null,
            composer.Spelling,
            canSend: ComposerCanSend(session, composer.SessionKey),
            composer.Armed,
            composer.Revision);
        Drawing(PanelIds.Composer, composer.WindowId, model);
        if (_dockedSessions.TryGetValue(composer.WindowId, out var native))
        {
            native.Publish(model);
        }

        return model;
    }

    /// <summary>
    /// The native editor's channel for a docked composer: its text written into the cells, ⌃S through
    /// <see cref="SendDocked"/>, ⌥L, ⌥W, Esc through <see cref="ReleaseComposer"/> and a click through
    /// <see cref="ArmComposer"/> — the methods the cell composer's keys and clicks end in.
    /// </summary>
    private ComposerSession DockedSessionFor(DockedComposer composer)
    {
        if (_dockedSessions.TryGetValue(composer.WindowId, out var known))
        {
            return known;
        }

        var session = new ComposerSession(
            composer.Model ?? DescribeDocked(composer),
            () => composer.Spelling,
            new ComposerCallbacks
            {
                Edited = body => OnUiThread(() => composer.SetFromHost(body)),
                Send = body => OnUiThread(() =>
                {
                    composer.SetFromHost(body);
                    SendDocked(composer);
                }),
                ToggleEscaping = () => OnUiThread(composer.ToggleEscaping),
                TogglePreview = () => OnUiThread(() => PreviewDocked(composer)),
                AddWord = word => OnUiThread(() => AddComposerWord(word)),
                Release = () => OnUiThread(() =>
                {
                    if (ReferenceEquals(ArmedComposer(), composer))
                    {
                        ReleaseComposer();
                    }
                }),
                Arm = () => OnUiThread(() => ArmComposer(composer)),
                Proportional = ComposerFaceOf(composer.SessionKey),
                ChooseFace = ChooseComposerFace(composer.SessionKey),
            });
        _dockedSessions[composer.WindowId] = session;
        return session;
    }

    /// <summary>
    /// The docked panel's Preview: the post as it will be sent, which is F1's as-sent band (⌥P) — so F1 opens on this
    /// composer's draft with the band showing, the way F1 from the panel opens it (<see cref="ToggleComposer"/>). The
    /// panel itself has no room for a second copy of the post. Refused, as F1 is, while another surface is open.
    /// </summary>
    internal void PreviewDocked(DockedComposer composer)
    {
        if (ComposerOpen || !_dockedComposers.ContainsKey(composer.WindowId))
        {
            return;
        }

        if (!string.Equals(ActiveWindowId(), composer.WindowId, StringComparison.Ordinal))
        {
            ArmComposer(composer);
            if (!string.Equals(ActiveWindowId(), composer.WindowId, StringComparison.Ordinal))
            {
                return;
            }
        }

        _composer.Preview = true;
        ToggleComposer();
    }

    /// <summary>
    /// A click in a docked composer's native editor: arm it, bringing its window to the front when it is not — what a
    /// click on its cell editor does (<see cref="ArmClickedComposer"/>). Refused while a modal owns the screen.
    /// </summary>
    private void ArmComposer(DockedComposer composer)
    {
        if (!_window.GetIsActive() || ComposerOpen)
        {
            return;
        }

        if (!string.Equals(ActiveWindowId(), composer.WindowId, StringComparison.Ordinal))
        {
            _pinningFocus = true;
            try
            {
                Activate(composer.WindowId);
            }
            finally
            {
                _pinningFocus = false;
            }
        }

        _composerArmWindow = composer.WindowId;
        _composerReleased = false;
        SyncComposerArm();
    }

    /// <summary>
    /// ⌥W in a docked composer's cells: adds the word the caret is in (or just after) to the dictionary, as F1's ⌥W.
    /// </summary>
    private void AddDockedWordAtCaret(DockedComposer composer)
    {
        var lines = composer.Body.Split('\n');
        var row = composer.Editor.CurrentLine - 1;
        if (row >= 0 && row < lines.Length && SpellChecker.WordAt(lines[row], composer.Editor.CurrentColumn - 1) is { } word)
        {
            AddComposerWord(word);
        }
    }

    /// <summary>The native editor's channel for the docked composer in <paramref name="windowId"/>, for the tests.</summary>
    internal ComposerSession? DockedComposerSession(string windowId) => _dockedSessions.GetValueOrDefault(windowId);

    /// <summary>F1 open in the host's editor: what it was opened for, and what it holds now. Read on the client's thread.</summary>
    private sealed class NativeCompose(string target, bool canSend, MooEdit? edit)
    {
        internal string Target { get; } = target;

        internal bool CanSend { get; } = canSend;

        internal MooEdit? Edit { get; } = edit;

        internal string Body { get; set; } = string.Empty;

        internal ComposeEscaping Escaping { get; set; }

        /// <summary>The script file this is an edit of, or null; its status moves with each save.</summary>
        internal ScriptEdit? Script { get; set; }

        internal ComposerSession Session { get; set; } = null!;
    }
}
