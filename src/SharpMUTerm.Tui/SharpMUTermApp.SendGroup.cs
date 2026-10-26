using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Tui;

/// <summary>
/// The send group: characters picked on ⌃P, marked ◆ on the rail, and sent one line together by
/// ⌃P ▸ <c>Send the command line to the group</c>. Lua reaches the same sessions by name through
/// <c>world.sendTo</c>.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private readonly SendGroup _sendGroup = new();

    /// <summary>The send group's members, for the tests.</summary>
    internal IReadOnlyList<string> SendGroupKeys => _sendGroup.Keys;

    /// <summary>Handles a <c>group:</c> id; false for any other.</summary>
    private bool DispatchGroupCommand(string id)
    {
        if (id.StartsWith(CommandIds.GroupTogglePrefix, StringComparison.Ordinal))
        {
            var key = id[CommandIds.GroupTogglePrefix.Length..];
            if (_sessions.Find(key) is null && !_sendGroup.Contains(key))
            {
                RefuseCommand($"{key} is not open");
                return true;
            }

            Notice(
                _sendGroup.Toggle(key) ? $"{key} is in the send group" : $"{key} has left the send group",
                MessageSeverity.Info);
            RefreshRail();
            return true;
        }

        if (id == CommandIds.GroupClear)
        {
            _sendGroup.Clear();
            RefreshRail();
            return true;
        }

        if (id == CommandIds.GroupSend)
        {
            SendToGroup();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sends what is on the command line to every connected member of the group, once each, as if typed in that
    /// character's own window, then clears the line the way ⏎ does. Members that are not connected get nothing
    /// and are named.
    /// </summary>
    private void SendToGroup()
    {
        var bar = ActiveBar();
        var command = bar.Text;
        if (command.Trim().Length == 0)
        {
            RefuseCommand("the command line is empty — type what the group should send first");
            return;
        }

        var (reachable, missed) = _sendGroup.Resolve(_sessions.Sessions);
        if (reachable.Count == 0)
        {
            RefuseCommand(_sendGroup.Count == 0 ? "the send group is empty" : "nobody in the send group is connected");
            return;
        }

        foreach (var session in reachable)
        {
            _ = session.SendUserInputAsync(command);
        }

        var kind = BarKind(bar);
        HistoryFor(kind).Add(command);
        bar.SetAndNotify(string.Empty);
        var windowId = ActiveWindowId();
        _drafts.Clear(windowId, kind);
        _workspace.SetUnsentInput(windowId, AnyBarHasText());
        RefreshTabTitles();

        var names = string.Join(", ", reachable.Select(s => s.Character?.Name ?? s.SessionKey));
        Notice(
            missed.Count == 0
                ? $"sent to {names}"
                : $"sent to {names}; not to {string.Join(", ", missed)}, not connected",
            missed.Count == 0 ? MessageSeverity.Info : MessageSeverity.Warning);
    }

    /// <summary>What <c>world.sendTo</c> does: null when the line went, or why it did not.</summary>
    private string? ScriptSendTo(string name, string command)
    {
        var (session, refusal) = SendGroup.Find(_sessions.Sessions, name);
        if (session is null)
        {
            return refusal;
        }

        _ = session.SendRawAsync(command);
        return null;
    }
}
