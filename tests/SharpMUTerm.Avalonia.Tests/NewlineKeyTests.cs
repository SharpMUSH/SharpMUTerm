namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// A window, unlike a terminal, reports Ctrl and Shift on Enter, so both reach the command line as the modified
/// ⏎ its key table already reads as a newline. Read off the client's frame: the caret goes to a new row and the draft is not sent.
/// </summary>
public class NewlineKeyTests
{
    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task CtrlOrShiftEnterBreaksTheLineInsteadOfSendingIt(bool ctrl, bool shift)
    {
        var (before, after) = ClientResizeTests.RunPair(93, 24, driver =>
            driver.RaiseKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift, false, ctrl)));

        var draftRow = Array.FindIndex(ClientResizeTests.Rows(before), r => r.Contains("say hello there"));
        await Assert.That(before.Caret.Y).IsEqualTo(draftRow);

        // Still drafted, not sent, and the caret has gone down a row to the start of the new one.
        // Below 100 columns the empty bar is one row and grows upwards by the row the newline added, so the draft is
        // read off the frame after the key rather than assumed to have stayed where it was.
        await Assert.That(ClientResizeTests.Rows(after).Count(r => r.Contains("say hello there"))).IsEqualTo(1);
        var draftAfter = Array.FindIndex(ClientResizeTests.Rows(after), r => r.Contains("say hello there"));
        await Assert.That(after.Caret.Y).IsEqualTo(draftAfter + 1);
        await Assert.That(after.Caret.X).IsLessThan(before.Caret.X);
    }
}
