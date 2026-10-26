using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Telnet;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// Editing a MOO's verbs, properties and notes in the composer (MCP <c>dns-org-mud-moo-simpleedit</c>). The MOO
/// hands over a buffer, F1 edits it, and ⌃S saves it back as lines.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>
    /// The buffers each character's MOO handed over and nobody has saved yet, by session key. One each: a MOO
    /// that sends another edit means the newer one, as <c>@edit</c> typed again does. Held apart from the post
    /// drafts so an edit arriving never replaces a post being written.
    /// </summary>
    private readonly Dictionary<string, ComposeResult> _mooEdits = new(StringComparer.Ordinal);

    /// <summary>The reserved width of the MOO status cell.</summary>
    internal const int MooStatusCellWidth = 28;

    /// <summary>The edit held for <paramref name="sessionKey"/>, for the tests.</summary>
    internal MooEdit? HeldMooEdit(string sessionKey) => _mooEdits.TryGetValue(sessionKey, out var held) ? held.Edit : null;

    /// <summary>
    /// A MOO handed over a buffer. It opens in the composer straight away when that character's window is the
    /// one in front and nothing else is open, since <c>@edit</c> was just typed there; otherwise it is held and
    /// the notice says F1 opens it, so a modal never lands over another window or another surface.
    /// </summary>
    private void ReceiveMooEdit(WorldSession session, MooEdit edit)
    {
        _mooEdits[session.SessionKey] = new ComposeResult(string.Join('\n', edit.Lines), ComposeEscaping.AsTyped, edit);
        if (!AnyOverlayOpen && ReferenceEquals(WindowSession(ActiveWindowId()), session))
        {
            ToggleComposer();
            return;
        }

        Notice($"{Snippet(SessionTitle(session))}'s MOO sent {Snippet(edit.Name)} to edit · F1 in that window opens it", MessageSeverity.Info);
    }

    /// <summary>
    /// Saves an edit back to the MOO: the buffer's lines as typed. The window closes first, which keeps the
    /// buffer as the held edit, and the edit is let go only once the MOO has it — a save that fails keeps it.
    /// </summary>
    private void SaveMooEdit(WorldSession session, MooEdit edit, string body)
    {
        _composer.Close();
        var key = session.SessionKey;
        var lines = MooEditing.LinesOf(body);
        _ = SaveAsync();

        async Task SaveAsync()
        {
            var sent = await session.SendMooEditAsync(edit, lines).ConfigureAwait(false);
            OnUiThread(() =>
            {
                if (!sent)
                {
                    RefuseCommand($"{Snippet(edit.Name)} was not saved — the MOO is not taking edits on this connection; F1 keeps it");
                    return;
                }

                if (_mooEdits.TryGetValue(key, out var held) && ReferenceEquals(held.Edit, edit))
                {
                    _mooEdits.Remove(key);
                }

                Notice($"saved {Snippet(edit.Name)}", MessageSeverity.Info);
            });
        }
    }

    /// <summary><c>» status</c>, elided and padded to <see cref="MooStatusCellWidth"/>. The text is the server's.</summary>
    internal static string MooStatusCell(string status)
    {
        var text = new string([.. status.Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
        var room = MooStatusCellWidth - 2;
        if (text.Length > room)
        {
            text = text[..(room - 1)] + "…";
        }

        return $"[dim]» {Escape(text.PadRight(room))}[/]";
    }
}
