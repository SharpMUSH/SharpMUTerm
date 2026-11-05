using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Logging;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Session;

/// <summary>GMCP channel capture, the highlight-names list and the scene log.</summary>
public class ChannelsAndNamesTests
{
    private static (WorldSession Session, FakeTelnetSession Telnet) Create(WorldDefinition world)
    {
        var telnet = new FakeTelnetSession();
        return (new WorldSession(world, sessionFactory: _ => telnet), telnet);
    }

    private static WorldDefinition World() => new() { Name = "T", Host = "h", Port = 1 };

    private const string Ooc = """{"channel":"ooc","talker":"Ashlyn","text":"\u001b[36m(OOC) Ashlyn: hello all\u001b[0m"}""";

    [Test]
    public async Task ChannelTextIsNotCapturedUnlessTheWorldSaysSo()
    {
        var (session, telnet) = Create(World());
        var spawned = new List<SpawnLineEventArgs>();
        session.SpawnLine += (_, e) => spawned.Add(e);
        await session.ConnectAsync();
        telnet.EmitGmcp("Comm.Channel.Text", Ooc);
        await Assert.That(spawned).IsEmpty();
    }

    [Test]
    public async Task CopyPutsTheChannelInItsWindowAndKeepsTheMainLine()
    {
        var world = World();
        world.CaptureChannels = ChannelCaptureMode.Copy;
        var (session, telnet) = Create(world);
        var spawned = new List<SpawnLineEventArgs>();
        var printed = new List<string>();
        session.SpawnLine += (_, e) => spawned.Add(e);
        session.LinePrinted += (_, l) => printed.Add(l.Text);
        await session.ConnectAsync();

        telnet.EmitGmcp("Comm.Channel.Text", Ooc);
        telnet.EmitLine("(OOC) Ashlyn: hello all");

        await Assert.That(spawned).HasSingleItem();
        await Assert.That(spawned[0].Target).IsEqualTo("ooc");
        await Assert.That(spawned[0].Line.Text).IsEqualTo("(OOC) Ashlyn: hello all");
        await Assert.That(spawned[0].Line.Spans[0].Style.Foreground == TerminalColor.Default).IsFalse();
        await Assert.That(printed).Contains("(OOC) Ashlyn: hello all");
    }

    [Test]
    public async Task MoveKeepsTheEchoOutOfTheMainWindowOnce()
    {
        var world = World();
        world.CaptureChannels = ChannelCaptureMode.Move;
        var (session, telnet) = Create(world);
        var printed = new List<string>();
        session.LinePrinted += (_, l) => printed.Add(l.Text);
        await session.ConnectAsync();

        telnet.EmitGmcp("Comm.Channel.Text", Ooc);
        telnet.EmitLine("(OOC) Ashlyn: hello all");
        telnet.EmitLine("(OOC) Ashlyn: hello all");

        await Assert.That(printed.Count(p => p == "(OOC) Ashlyn: hello all")).IsEqualTo(1);
    }

    /// <summary>
    /// A server that prints the channel line before its GMCP message, in the same read: the line waits for the rest of
    /// the read, so the echo is still kept out, and the lines around it keep their order.
    /// </summary>
    [Test]
    public async Task MoveCatchesAnEchoPrintedBeforeItsMessageInTheSameRead()
    {
        var world = World();
        world.CaptureChannels = ChannelCaptureMode.Move;
        var (session, telnet) = Create(world);
        var printed = new List<string>();
        session.LinePrinted += (_, l) => printed.Add(l.Text);
        await session.ConnectAsync();
        printed.Clear();

        telnet.EmitLine("The fountain splashes.", endRead: false);
        telnet.EmitLine("(OOC) Ashlyn: hello all", endRead: false);
        await Assert.That(printed).IsEmpty().Because("held until the read is done");

        telnet.EmitGmcp("Comm.Channel.Text", Ooc);
        telnet.EmitLine("A pigeon lands.", endRead: false);
        telnet.EndRead();

        await Assert.That(string.Join(" | ", printed)).IsEqualTo("The fountain splashes. | A pigeon lands.");
    }

    /// <summary>The hold is a read long: a line whose read ended with no message is shown, in order, at once.</summary>
    [Test]
    public async Task MoveShowsAnUnmatchedLineWhenItsReadEnds()
    {
        var world = World();
        world.CaptureChannels = ChannelCaptureMode.Move;
        var (session, telnet) = Create(world);
        var printed = new List<string>();
        session.LinePrinted += (_, l) => printed.Add(l.Text);
        await session.ConnectAsync();
        printed.Clear();

        telnet.EmitLine("(OOC) Ashlyn: hello all", endRead: false);
        telnet.EmitPrompt("> ");
        telnet.EmitGmcp("Comm.Channel.Text", Ooc);

        await Assert.That(printed.First()).IsEqualTo("(OOC) Ashlyn: hello all").Because("a prompt lets the held line go first, and a message after its read is too late");
    }

    [Test]
    public async Task AChannelNameIsMadeSafeForATitle()
    {
        await Assert.That(ChannelCapture.WindowName("ooc:\u001b[1m/../x")).IsEqualTo("ooc1mx");
        await Assert.That(ChannelCapture.Read("""{"channel":"","text":"x"}""")).IsNull();
        await Assert.That(ChannelCapture.Read("not json")).IsNull();
        await Assert.That(ChannelCapture.Read("""{"channel":"ooc"}""")).IsNull();
    }

    [Test]
    public async Task ListedNamesAreHighlightedAsWholeWords()
    {
        var world = World();
        world.HighlightNames = [new NameHighlight("Ann", "red")];
        var (session, telnet) = Create(world);
        StyledLine? printed = null;
        session.LinePrinted += (_, l) => printed = l;
        await session.ConnectAsync();

        telnet.EmitLine("Annabel waves to Ann.");

        WebColors.TryParse("red", out var red);
        var spans = printed!.Spans;
        await Assert.That(spans.Any(s => s.Text == "Ann" && s.Style.Foreground == red)).IsTrue();
        await Assert.That(spans.Any(s => s.Text.Contains("Annabel") && s.Style.Foreground == red)).IsFalse();
    }

    [Test]
    public async Task TheListParsesAndFormats()
    {
        var (names, error) = NameHighlights.Parse("Ashlyn=gold; Merrow : red, Vale");
        await Assert.That(error).IsNull();
        await Assert.That(names.Select(n => n.Name)).IsEquivalentTo(["Ashlyn", "Merrow", "Vale"]);
        await Assert.That(names[2].Colour).IsEqualTo(NameHighlights.DefaultColour);
        await Assert.That(NameHighlights.Format(names)).IsEqualTo("Ashlyn, Merrow=red, Vale");
        await Assert.That(NameHighlights.Parse("Ann=notacolour").Error).IsNotNull();
        await Assert.That(NameHighlights.Parse("").Names).IsEmpty();
    }

    [Test]
    public async Task ASceneLogKeepsPosesAndNamesWhoWasPresent()
    {
        var folder = Directory.CreateTempSubdirectory("smt-scene-");
        try
        {
            var log = SceneLog.Start(folder.FullName, "main", "Tea at the Plaza", html: false, SceneLogContent.Poses, ["Ashlyn", "Corvid"], DateTimeOffset.Now);
            await Assert.That(log.Offer(StyledLine.FromText("Ashlyn sips her tea.", TextStyle.Default))).IsTrue();
            await Assert.That(log.Offer(StyledLine.FromText("The Grand Plaza", TextStyle.Default))).IsFalse();
            await Assert.That(log.Offer(StyledLine.FromText("Merrow says, \"Hello.\"", TextStyle.Default))).IsTrue();
            log.Stop(DateTimeOffset.Now);

            var text = File.ReadAllText(log.Path);
            await Assert.That(Path.GetFileName(log.Path)).StartsWith("scene-tea-at-the-plaza-");
            await Assert.That(text).Contains("Present: Ashlyn, Corvid");
            await Assert.That(text).Contains("Ashlyn sips her tea.");
            await Assert.That(text).DoesNotContain("The Grand Plaza");
            await Assert.That(text).Contains("posed: Ashlyn, Merrow");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ASceneLogInHtmlKeepsEveryLineWhenAsked()
    {
        var folder = Directory.CreateTempSubdirectory("smt-scene-");
        try
        {
            var log = SceneLog.Start(folder.FullName, "main", "Scene", html: true, SceneLogContent.Everything, [], DateTimeOffset.Now);
            log.Offer(StyledLine.FromText("The Grand Plaza", TextStyle.Default));
            log.Stop(DateTimeOffset.Now);
            var text = File.ReadAllText(log.Path);
            await Assert.That(log.Path).EndsWith(".html");
            await Assert.That(text).Contains("The Grand Plaza");
            await Assert.That(text).Contains("Present: nobody");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
