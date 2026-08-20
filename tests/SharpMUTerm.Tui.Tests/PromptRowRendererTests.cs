using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The row that carries the server's own prompt. Its whole reason for existing is that
/// <c>WorldSession.CurrentPrompt</c> had no reader: a prompt this client received correctly was
/// displayed nowhere, so a login screen ending in <c>IAC GA</c> looked like a server that had
/// stopped answering.
/// </summary>
public class PromptRowRendererTests
{
    private const string Band = "#22262e";

    private static MarkupFormatter Formatter() => new(ThemeLibrary.Get("Dark"));

    private static StyledLine Line(string text) =>
        new([new StyledSpan(text, TextStyle.Default)]);

    [Test]
    public async Task NoPrompt_RendersNothing()
    {
        await Assert.That(PromptRowRenderer.Row(null, Formatter(), 80, Band))
            .IsEqualTo(PromptRowRenderer.Empty);
    }

    [Test]
    public async Task EmptyPrompt_RendersNothing()
    {
        await Assert.That(PromptRowRenderer.Row(StyledLine.Empty, Formatter(), 80, Band))
            .IsEqualTo(PromptRowRenderer.Empty);
    }

    [Test]
    public async Task Prompt_CarriesTheTextAndTheBand()
    {
        var row = PromptRowRenderer.Row(Line("Enter name: "), Formatter(), 80, Band);

        await Assert.That(row).Contains("Enter name: ");
        await Assert.That(row).StartsWith($"[on {Band}]");
    }

    /// <summary>
    /// The band has to reach the right edge, or the row reads as a stray coloured word on the
    /// backdrop rather than as part of the input area.
    /// </summary>
    [Test]
    public async Task Prompt_IsPaddedToTheFullWidth()
    {
        var row = PromptRowRenderer.Row(Line("hi"), Formatter(), 20, Band);

        await Assert.That(MarkupText.VisibleLength(row)).IsEqualTo(20);
    }

    /// <summary>
    /// Elided rather than wrapped, and the ellipsis counts against the budget — a second row would
    /// come off every pane and re-announce a new terminal size to every connected game.
    /// </summary>
    [Test]
    public async Task LongPrompt_IsElidedIntoOneRow()
    {
        var row = PromptRowRenderer.Row(Line(new string('x', 200)), Formatter(), 40, Band);

        await Assert.That(MarkupText.VisibleLength(row)).IsEqualTo(40);
        await Assert.That(row).Contains("…");
    }

    /// <summary>
    /// A server may put a newline inside what it then ends with GA. The row is one row by
    /// construction, and what a prompt asks is at its end.
    /// </summary>
    [Test]
    public async Task MultiLinePrompt_ShowsTheLastSegment()
    {
        var row = PromptRowRenderer.Row(Line("Welcome!\r\nEnter name: "), Formatter(), 80, Band);

        await Assert.That(row).Contains("Enter name: ");
        await Assert.That(row).DoesNotContain("Welcome!");
    }

    [Test]
    public async Task ZeroWidth_RendersNothing()
    {
        await Assert.That(PromptRowRenderer.Row(Line("Enter name: "), Formatter(), 0, Band))
            .IsEqualTo(PromptRowRenderer.Empty);
    }
}
