using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Protocols;

/// <summary>
/// MXP <c>&lt;IMAGE&gt;</c>: the parser's half. It turns the tag into a <c>[image: name]</c> span whose
/// interaction carries the request; drawing the picture is the UI's half and is tested there.
/// </summary>
public class MxpImageTests
{
    private const string Secure = "\x1b[1z";

    /// <summary>Feeds one line the way a session does — text, then <c>Flush</c> as the boundary.</summary>
    private static StyledLine Line(string text)
    {
        var parser = new MxpParser();
        parser.Feed(text);
        return parser.Flush() ?? throw new InvalidOperationException("no line");
    }

    private static StyledSpan ImageSpan(StyledLine line) =>
        line.Spans.Single(s => s.Interaction?.Image is not null);

    [Test]
    public async Task AnImageBecomesALinkedPlaceholderCarryingItsRequest()
    {
        var line = Line(Secure + "Map: <IMAGE map.png URL=\"http://mud.example/img/\" W=200 H=10c>");

        await Assert.That(line.Text).IsEqualTo("Map: [image: map.png]");
        var span = ImageSpan(line);
        await Assert.That(span.Interaction!.Kind).IsEqualTo(InteractionKind.Hyperlink);
        await Assert.That(span.Interaction.Target).IsEqualTo("http://mud.example/img/map.png");
        await Assert.That(span.Interaction.Image).IsEqualTo(new InlineImageRequest(
            "http://mud.example/img/map.png",
            "map.png",
            new ImageExtent(200, ImageExtentUnit.Pixels),
            new ImageExtent(10, ImageExtentUnit.Characters)));
    }

    /// <summary>Spec: "The classname is appended to the URL, along with the name of the graphics file".</summary>
    [Test]
    public async Task TheTypeIsAppendedToTheUrlBeforeTheFileName()
    {
        var line = Line(Secure + "<IMAGE FName=\"rose.jpg\" URL=\"https://mud.example/media\" T=\"flowers\">");

        await Assert.That(ImageSpan(line).Interaction!.Image!.Url)
            .IsEqualTo("https://mud.example/media/flowers/rose.jpg");
    }

    [Test]
    public async Task AnAbsoluteFileNameNeedsNoUrl()
    {
        var line = Line(Secure + "<IMAGE https://mud.example/maps/area1.png ISMAP>");

        var request = ImageSpan(line).Interaction!.Image!;
        await Assert.That(request.Url).IsEqualTo("https://mud.example/maps/area1.png");
        await Assert.That(request.Name).IsEqualTo("area1.png");
    }

    /// <summary>
    /// The spec's own example. The picture keeps the SEND's action, so clicking it sends the command
    /// rather than opening the file.
    /// </summary>
    [Test]
    public async Task AnImageInsideASendKeepsTheSendsCommand()
    {
        var line = Line(Secure + "<SEND showmap><IMAGE map.jpg URL=\"http://mud.example/\" ISMAP></SEND>");

        var span = ImageSpan(line);
        await Assert.That(span.Interaction!.Kind).IsEqualTo(InteractionKind.SendCommand);
        await Assert.That(span.Interaction.Target).IsEqualTo("showmap");
        await Assert.That(span.Interaction.Image!.Url).IsEqualTo("http://mud.example/map.jpg");
    }

    /// <summary>
    /// With no URL the file is in a local media directory this client does not have: the placeholder
    /// is shown and nothing is asked for.
    /// </summary>
    [Test]
    public async Task ARelativeFileWithNoUrlIsAPlaceholderAndNothingElse()
    {
        var line = Line(Secure + "<IMAGE map.png>");

        await Assert.That(line.Text).IsEqualTo("[image: map.png]");
        await Assert.That(line.Spans.All(s => s.Interaction is null)).IsTrue();
    }

    [Test]
    [Arguments("file:///etc/passwd")]
    [Arguments("javascript:alert(1)")]
    [Arguments("ftp://mud.example/map.png")]
    public async Task OnlyWebSchemesAreFetched(string source)
    {
        var line = Line(Secure + $"<IMAGE \"{source}\">");

        await Assert.That(line.Spans.Any(s => s.Interaction?.Image is not null)).IsFalse();
    }

    [Test]
    public async Task AUrlOfAnotherSchemeIsRefusedEvenWithARelativeName()
    {
        var line = Line(Secure + "<IMAGE map.png URL=\"file:///home/me/\">");

        await Assert.That(line.Spans.Any(s => s.Interaction?.Image is not null)).IsFalse();
    }

    /// <summary>
    /// IMAGE is not on the spec's list of open tags, so a player cannot make every other client in the
    /// room fetch a URL of their choosing: on an open line the tag comes back as the text they typed.
    /// </summary>
    [Test]
    public async Task OnAnOpenLineTheTagIsEchoedLiterally()
    {
        var line = Line("Rivane says, '<IMAGE x.png URL=http://evil.example/>'");

        await Assert.That(line.Text).IsEqualTo("Rivane says, '<IMAGE x.png URL=http://evil.example/>'");
        await Assert.That(line.Spans.Any(s => s.Interaction is not null)).IsFalse();
    }

    [Test]
    public async Task SupportsNamesImage()
    {
        var parser = new MxpParser();
        var replies = new List<string>();
        parser.ClientReply += (_, r) => replies.Add(r);

        parser.Feed(Secure + "<SUPPORT>");
        parser.Flush();

        await Assert.That(replies.Single()).Contains("+image");
    }

    [Test]
    [Arguments("200", 200, ImageExtentUnit.Pixels)]
    [Arguments("200px", 200, ImageExtentUnit.Pixels)]
    [Arguments("12c", 12, ImageExtentUnit.Characters)]
    [Arguments("50%", 50, ImageExtentUnit.Percent)]
    public async Task ExtentsReadAllThreeUnits(string text, int value, ImageExtentUnit unit)
    {
        await Assert.That(ImageExtent.TryParse(text, out var extent)).IsTrue();
        await Assert.That(extent).IsEqualTo(new ImageExtent(value, unit));
    }

    [Test]
    [Arguments("")]
    [Arguments("0")]
    [Arguments("-5")]
    [Arguments("150%")]
    [Arguments("wide")]
    [Arguments("99999999")]
    public async Task NonsenseExtentsAreIgnored(string text)
    {
        await Assert.That(ImageExtent.TryParse(text, out _)).IsFalse();
    }

    [Test]
    public async Task ALongNameIsElided()
    {
        var name = MxpImageSource.DisplayName(new string('a', 100) + ".png");

        await Assert.That(name.Length).IsEqualTo(MxpImageSource.MaxNameLength);
        await Assert.That(name).EndsWith("…");
    }
}
