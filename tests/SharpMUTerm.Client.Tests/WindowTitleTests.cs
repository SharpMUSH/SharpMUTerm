using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Client.Tests;

/// <summary>The one title both hosts show for what is in front: <c>Character · World — SharpMUTerm</c>.</summary>
public class WindowTitleTests
{
    [Test]
    [Arguments("Corvid", "Aetherfall", "Corvid · Aetherfall — SharpMUTerm")]
    [Arguments(null, "Aetherfall", "Aetherfall — SharpMUTerm")]
    [Arguments("", "Aetherfall", "Aetherfall — SharpMUTerm")]
    [Arguments(null, null, "SharpMUTerm")]
    [Arguments("  ", " ", "SharpMUTerm")]
    public async Task TheTitleNamesWhoIsInFrontAndWhere(string? character, string? world, string expected) =>
        await Assert.That(WindowTitle.For(character, world)).IsEqualTo(expected);

    [Test]
    public async Task TheTitleIsReadOffTheHeadersOwnModel()
    {
        var header = HeaderModel.Empty with
        {
            World = new HeaderWorld("Aetherfall", TerminalColor.Default),
            Character = new HeaderCharacter("Corvid", "Aetherfall.Corvid", CharacterPresence.Online, "aetherfall.mux", 4201, false, "x"),
        };

        await Assert.That(WindowTitle.For(header)).IsEqualTo("Corvid · Aetherfall — SharpMUTerm");
        await Assert.That(WindowTitle.For(HeaderModel.Empty)).IsEqualTo(WindowTitle.Product);
    }

    /// <summary>A title is written into an escape sequence in a terminal, where a BEL or ESC would end it early.</summary>
    [Test]
    public async Task ControlCharactersAreDroppedAndLongNamesAreCut()
    {
        var title = WindowTitle.For("Cor\u0007vid\u001b]2;x", "evil\r\nworld");

        await Assert.That(title).IsEqualTo("Corvid]2;x · evilworld — SharpMUTerm");
        await Assert.That(title.Any(char.IsControl)).IsFalse();

        var longName = new string('n', 200);
        var cut = WindowTitle.For(longName, "w");
        await Assert.That(cut).StartsWith(new string('n', WindowTitle.MaxPart - 1) + "… · w");
    }

    /// <summary>
    /// One spelling of a character's name everywhere: the title, the prompt and a tab's owner prefix all read
    /// <see cref="CharacterNames"/>'s, so none of them can drift back to <c>Corvid@Aetherfall</c> or <c>Corvid - Chat</c>.
    /// </summary>
    [Test]
    public async Task TheTitleThePromptAndATabsOwnerSpellANameTheSameWay()
    {
        var name = CharacterNames.Display("Aetherfall", "Corvid");

        await Assert.That(name).IsEqualTo("Corvid · Aetherfall");
        await Assert.That(WindowTitle.For("Corvid", "Aetherfall")).IsEqualTo(name + " — " + WindowTitle.Product);
        await Assert.That(StatusFormatter.CharacterPrompt("Corvid", "Aetherfall")).IsEqualTo(name + " › ");
        await Assert.That(CharacterNames.Window("Corvid", "Chat")).IsEqualTo("Corvid" + CharacterNames.Separator + "Chat");
        await Assert.That(CharacterNames.Window(null, "Chat")).IsEqualTo("Chat");
    }
}
