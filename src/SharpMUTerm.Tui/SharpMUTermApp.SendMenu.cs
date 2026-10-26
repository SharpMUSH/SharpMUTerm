using SharpMUTerm.Core.Text;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// An MXP <c>&lt;SEND HREF="a|b|c"&gt;</c> menu. A click sends the first command; a right-click offers them
/// all — in the window's own menu, and in a terminal, which has none, in this chooser.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private readonly ListOverlay _sendMenu;

    private int _sendMenuSelected;

    internal ListOverlay SendMenu => _sendMenu;

    /// <summary>Sends, or puts on the command line, one command of a menu, as a click on a one-command link would.</summary>
    private void RunSendChoice(string windowId, LinkAction action, string command) =>
        OnLinkClicked(windowId, (action == LinkAction.Prompt ? LinkPayload.PromptScheme : LinkPayload.SendScheme) + command);

    /// <summary>The chooser: the menu's labels, ↑↓ and ⏎, or a digit for one of the first nine.</summary>
    internal void ShowSendMenu(string windowId, LinkPayload menu)
    {
        if (menu.Choices is not { Count: > 0 } choices || AnyOverlayOpen)
        {
            return;
        }

        _sendMenuSelected = 0;
        var to = WindowSession(windowId) is { } session ? SessionTitle(session) : null;
        _sendMenu.Open(
            "menu",
            menu.Action == LinkAction.Prompt ? "Put on the command line" : to is null ? "Send" : $"Send to {Snippet(to)}",
            width => SendMenuLines(choices, _sendMenuSelected, width),
            key =>
            {
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        _sendMenuSelected = Math.Max(0, _sendMenuSelected - 1);
                        return true;
                    case ConsoleKey.DownArrow:
                        _sendMenuSelected = Math.Min(choices.Count - 1, _sendMenuSelected + 1);
                        return true;
                    case ConsoleKey.Enter:
                        RunSendChoice(windowId, menu.Action, choices[_sendMenuSelected].Command);
                        return false;
                    case >= ConsoleKey.D1 and <= ConsoleKey.D9 when key.Key - ConsoleKey.D1 < choices.Count:
                        RunSendChoice(windowId, menu.Action, choices[key.Key - ConsoleKey.D1].Command);
                        return false;
                    default:
                        return true;
                }
            },
            width: SendMenuLines(choices, 0, int.MaxValue).Max(MarkupText.VisibleLength));
    }

    internal static List<string> SendMenuLines(IReadOnlyList<SendChoice> choices, int selected, int width)
    {
        var lines = new List<string>();
        var column = choices.Max(c => Snippet(c.Label).Length) + 2;
        for (var i = 0; i < choices.Count; i++)
        {
            var key = i < 9 ? $"{i + 1}" : " ";
            var mark = i == selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            var label = Snippet(choices[i].Label);
            var command = choices[i].Command == choices[i].Label
                ? string.Empty
                : $"[{ScreenPalette.Label}]{Escape(Snippet(choices[i].Command))}[/]";
            lines.Add($"{mark} [{ScreenPalette.Label}]{key}[/] [{(i == selected ? "bold " : string.Empty)}{ScreenPalette.Value}]{Escape(label.PadRight(column))}[/]{command}");
        }

        lines.Add(string.Empty);
        lines.Add($"[{ScreenPalette.Label}]↑↓ select · ⏎ or a digit sends · Esc cancels[/]");
        return lines;
    }

    /// <summary>The first cell of a <c>&lt;SEND&gt;</c> menu in <paramref name="windowId"/>'s pane, read off the arranged frame; for the demo and tests.</summary>
    internal System.Drawing.Point? MenuLinkCell(string windowId)
    {
        var size = _system.ConsoleDriver.ScreenSize;
        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                var cell = new System.Drawing.Point(x, y);
                if (TargetAt(cell) is { Kind: PointerKind.Link } target && LinkOf(target) is { Payload.Choices: not null } link && link.WindowId == windowId)
                {
                    return cell;
                }
            }
        }

        return null;
    }
}
