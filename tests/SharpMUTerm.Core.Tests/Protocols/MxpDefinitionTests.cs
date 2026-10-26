using SharpMUTerm.Core.Mapping;
using SharpMUTerm.Core.Media;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Protocols;

/// <summary>
/// What a server defines and declares over MXP — elements, entities, VAR, GAUGE, SOUND, FRAME and DEST — each fed
/// the way a session feeds it: one line, then a flush.
/// </summary>
public class MxpDefinitionTests
{
    private const string Secure = "\x1b[1z";

    private static List<StyledLine?> Lines(MxpParser parser, params string[] lines)
    {
        var result = new List<StyledLine?>();
        foreach (var text in lines)
        {
            parser.Feed(text);
            result.Add(parser.Flush());
        }

        return result;
    }

    // ----- Reading a tag ------------------------------------------------------------

    [Test]
    public async Task AnUnclosedTagEndsWithItsLine()
    {
        var parser = new MxpParser();
        var lines = Lines(parser, "a < b and <c", "next line");

        await Assert.That(lines[0]!.Text).IsEqualTo("a < b and <c");
        await Assert.That(lines[1]!.Text).IsEqualTo("next line");
    }

    [Test]
    public async Task AQuoteHoldsATagOpenOnlyWhereAValueCanStart()
    {
        var parser = new MxpParser();
        var lines = Lines(parser, Secure + "<COLOR fore=red hint='a>b'>x</COLOR>", "<don't>y");

        await Assert.That(lines[0]!.Text).IsEqualTo("x");
        await Assert.That(lines[1]!.Text).IsEqualTo("<don't>y");
    }

    // ----- <!ELEMENT> ----------------------------------------------------------

    [Test]
    public async Task AnElementOpensItsDefinitionAndItsCloserClosesIt()
    {
        var parser = new MxpParser();
        var lines = Lines(parser, Secure + "<!ELEMENT warn '<COLOR red><B>'>", Secure + "<warn>Danger</warn> ahead");

        await Assert.That(lines[0]).IsNull();
        await Assert.That(parser.LineSwallowed).IsFalse(); // the second line is text
        var danger = lines[1]!.Spans.First(s => s.Text == "Danger");
        await Assert.That(danger.Style.Attributes.HasFlag(TextAttributes.Bold)).IsTrue();
        await Assert.That(lines[1]!.Spans.First(s => s.Text.Contains("ahead")).Style.Attributes.HasFlag(TextAttributes.Bold)).IsFalse();
    }

    [Test]
    public async Task ALineThatIsOnlyDefinitionsIsSwallowedNotBlank()
    {
        var parser = new MxpParser();

        parser.Feed(Secure + "<!ELEMENT a '<B>'><!ENTITY hp 10>");
        await Assert.That(parser.Flush()).IsNull();
        await Assert.That(parser.LineSwallowed).IsTrue();

        parser.Feed(string.Empty);
        await Assert.That(parser.Flush()).IsNull();
        await Assert.That(parser.LineSwallowed).IsFalse();
    }

    [Test]
    public async Task AttributesAreFilledInByNameByPositionOrByDefault()
    {
        var parser = new MxpParser();
        var lines = Lines(
            parser,
            Secure + "<!ELEMENT go '<SEND href=\"go &dir;\" hint=\"&tip;\">' ATT='dir tip=Walk'>",
            Secure + "<go north>N</go> <go dir=south tip=Run>S</go>");

        var spans = lines[1]!.Spans.Where(s => s.IsInteractive).ToArray();
        await Assert.That(spans[0].Interaction!.Target).IsEqualTo("go north");
        await Assert.That(spans[0].Interaction!.Hint).IsEqualTo("Walk");
        await Assert.That(spans[1].Interaction!.Target).IsEqualTo("go south");
        await Assert.That(spans[1].Interaction!.Hint).IsEqualTo("Run");
    }

    [Test]
    public async Task ADefinitionOnAnOpenLineIsRefusedAndEchoed()
    {
        var parser = new MxpParser();
        var lines = Lines(parser, "<!ELEMENT evil '<SEND quit>'>", Secure + "<evil>x</evil>");

        await Assert.That(lines[0]!.Text).IsEqualTo("<!ELEMENT evil '<SEND quit>'>");
        await Assert.That(parser.ElementCount).IsEqualTo(0);
        await Assert.That(lines[1]!.Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    [Test]
    public async Task APlayerMayUseAnOpenElementButItCannotCarryASecureTag()
    {
        var parser = new MxpParser();
        var lines = Lines(
            parser,
            Secure + "<!ELEMENT hl '<COLOR &c;><SEND quit>' ATT='c' OPEN>",
            "<hl c=red>look</hl>",
            "<hl c='red><SEND quit'>look</hl>");

        var look = lines[1]!.Spans.Single(s => s.Text == "look");
        await Assert.That(look.Style.Foreground).IsNotEqualTo(TerminalColor.Default);
        await Assert.That(look.IsInteractive).IsFalse();
        await Assert.That(lines[2]!.Spans.Any(s => s.IsInteractive)).IsFalse();
        await Assert.That(lines[2]!.Text).IsEqualTo("look");
    }

    [Test]
    public async Task ANonOpenElementOnAnOpenLineIsEchoed()
    {
        var parser = new MxpParser();
        var lines = Lines(parser, Secure + "<!ELEMENT b2 '<B>'>", "<b2>x</b2>");

        await Assert.That(lines[1]!.Text).IsEqualTo("<b2>x</b2>");
    }

    [Test]
    public async Task ABuiltInCannotBeRedefined()
    {
        var parser = new MxpParser();
        Lines(parser, Secure + "<!ELEMENT SEND '<B>'>", Secure + "<!ELEMENT color '<B>' OPEN>", Secure + "<!EL bold '<I>'>");

        await Assert.That(parser.ElementCount).IsEqualTo(0);
    }

    [Test]
    public async Task ExpansionIsCappedInDepthAndPerLine()
    {
        var parser = new MxpParser();
        Lines(
            parser,
            Secure + "<!ELEMENT a '<b><b><b><b>'>",
            Secure + "<!ELEMENT b '<c><c><c><c>'>",
            Secure + "<!ELEMENT c '<d><d><d><d>'>",
            Secure + "<!ELEMENT d '<e><e><e><e>'>",
            Secure + "<!ELEMENT e '<a><a><a><a>'>");

        var line = Lines(parser, Secure + string.Concat(Enumerable.Repeat("<a>", 50)) + "done")[0];

        await Assert.That(line!.Text).IsEqualTo("done");
    }

    [Test]
    public async Task ALongDefinitionAndTooManyElementsAreRefused()
    {
        var parser = new MxpParser();
        Lines(parser, Secure + $"<!ELEMENT big '{new string('x', MxpParser.MaxDefinitionLength + 1)}'>");
        await Assert.That(parser.ElementCount).IsEqualTo(0);

        Lines(parser, Enumerable.Range(0, MxpParser.MaxElements + 10).Select(i => Secure + $"<!ELEMENT e{i} '<B>'>").ToArray());
        await Assert.That(parser.ElementCount).IsEqualTo(MxpParser.MaxElements);
    }

    [Test]
    public async Task DeleteRemovesAnElement()
    {
        var parser = new MxpParser();
        Lines(parser, Secure + "<!ELEMENT a '<B>'>", Secure + "<!ELEMENT a DELETE>");

        await Assert.That(parser.ElementCount).IsEqualTo(0);
    }

    [Test]
    public async Task AReconnectStartsWithoutDefinitions()
    {
        var parser = new MxpParser();
        Lines(parser, Secure + "<!ELEMENT a '<B>'>", Secure + "<!ENTITY hp 5>");

        parser.Reset();

        await Assert.That(parser.ElementCount).IsEqualTo(0);
        await Assert.That(parser.Entity("hp")).IsNull();
    }

    // ----- Entities -------------------------------------------------------------

    [Test]
    public async Task AnEntityIsSetExpandedAddedToRemovedAndDeleted()
    {
        var parser = new MxpParser();
        var changes = new List<MxpEntityChange>();
        parser.EntityChanged += (_, e) => changes.Add(e);

        var lines = Lines(
            parser,
            Secure + "<!ENTITY hp 42>",
            "You have &hp; hit points",
            Secure + "<!ENTITY list a><!ENTITY list b ADD><!ENTITY list a REMOVE>",
            Secure + "<!ENTITY hp DELETE>");

        await Assert.That(lines[1]!.Text).IsEqualTo("You have 42 hit points");
        await Assert.That(changes.Select(c => (c.Name, c.Value))).IsEquivalentTo(
            new (string, string?)[] { ("hp", "42"), ("list", "a"), ("list", "a|b"), ("list", "b"), ("hp", null) },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task APrivateEntityExpandsButIsNotAnnounced()
    {
        var parser = new MxpParser();
        var changes = new List<MxpEntityChange>();
        parser.EntityChanged += (_, e) => changes.Add(e);

        var lines = Lines(parser, Secure + "<!ENTITY secret x PRIVATE>", "&secret;");

        await Assert.That(lines[1]!.Text).IsEqualTo("x");
        await Assert.That(changes).IsEmpty();
    }

    [Test]
    public async Task ABuiltInEntityCannotBeRedefinedAndAnEntityIsTextNotMarkup()
    {
        var parser = new MxpParser();
        var lines = Lines(parser, Secure + "<!ENTITY lt X>", Secure + "<!ENTITY tag '<SEND quit>'>", Secure + "&lt;&tag;");

        await Assert.That(lines[2]!.Text).IsEqualTo("<<SEND quit>");
        await Assert.That(lines[2]!.Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    [Test]
    public async Task VarShowsItsTextAndStoresIt()
    {
        var parser = new MxpParser();
        var lines = Lines(parser, Secure + "Gold: <VAR gold>120</VAR>");

        await Assert.That(lines[0]!.Text).IsEqualTo("Gold: 120");
        await Assert.That(parser.Entity("gold")).IsEqualTo("120");
    }

    [Test]
    public async Task AnElementFlaggedSetStoresItsText()
    {
        var parser = new MxpParser();
        Lines(parser, Secure + "<!ELEMENT hpv '' FLAG='set hp'>", Secure + "HP: <hpv>17</hpv>");

        await Assert.That(parser.Entity("hp")).IsEqualTo("17");
    }

    [Test]
    public async Task VarOnAnOpenLineIsEchoed()
    {
        var parser = new MxpParser();
        var lines = Lines(parser, "<VAR gold>1</VAR>");

        await Assert.That(lines[0]!.Text).IsEqualTo("<VAR gold>1</VAR>");
        await Assert.That(parser.Entity("gold")).IsNull();
    }

    // ----- Flags and the room source ---------------------------------------------

    [Test]
    public async Task RoomFlagsReachTheRoomSource()
    {
        var parser = new MxpParser();
        var flags = new List<MxpFlagText>();
        parser.Flagged += (_, e) => flags.Add(e);

        Lines(
            parser,
            Secure + "<!ELEMENT RName '<B>' FLAG=RoomName><!ELEMENT RExits FLAG=RoomExit><!ELEMENT RNum FLAG=RoomNum EMPTY>",
            Secure + "<RNum>1234</RNum><RName>The Grand Plaza</RName>",
            Secure + "<RExits>Exits: north, east and down</RExits>");

        await Assert.That(flags.Select(f => f.Flag)).IsEquivalentTo(
            new[] { "RoomName", "RoomExit" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        RoomObservation? seen = null;
        using var source = new MxpRoomSourceProbe();
        source.Source.RoomEntered += (_, o) => seen = o;
        source.Source.Observe("RoomNum", "1234");
        source.Source.Observe("RoomName", "The Grand Plaza");
        source.Source.Observe("RoomExit", "Exits: north, east and down");

        await Assert.That(seen!.Id).IsEqualTo(1234L);
        await Assert.That(seen.Name).IsEqualTo("The Grand Plaza");
        await Assert.That(seen.Exits.Select(e => e.Key)).IsEquivalentTo(
            new[] { "north", "east", "down" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task TheSpecsRoomLineTagsFlagTheirLine()
    {
        var parser = new MxpParser();
        var flags = new List<MxpFlagText>();
        parser.Flagged += (_, e) => flags.Add(e);

        var lines = Lines(parser, "\x1b[10zThe Grand Plaza", "\x1b[12zExits: north");

        await Assert.That(lines[0]!.Text).IsEqualTo("The Grand Plaza");
        await Assert.That(flags.Select(f => (f.Flag, f.Text))).IsEquivalentTo(
            new[] { ("RoomName", "The Grand Plaza"), ("RoomExit", "Exits: north") },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AFlaggedElementSpanningLinesKeepsAllItsText()
    {
        var parser = new MxpParser();
        var flags = new List<MxpFlagText>();
        parser.Flagged += (_, e) => flags.Add(e);

        Lines(parser, Secure + "<!ELEMENT RDesc FLAG=RoomDesc>", Secure + "<RDesc>A wide plaza.", Secure + "A fountain.</RDesc>");

        await Assert.That(flags.Single().Text).IsEqualTo("A wide plaza.\nA fountain.");
    }

    [Test]
    public async Task AUserLineTagOpensTheElementTiedToIt()
    {
        var parser = new MxpParser();
        var lines = Lines(parser, Secure + "<!ELEMENT hdr '<B>' TAG=20>", "\x1b[20zHeading", "plain");

        await Assert.That(lines[1]!.Spans.Single().Style.Attributes.HasFlag(TextAttributes.Bold)).IsTrue();
        await Assert.That(lines[2]!.Spans.Single().Style.Attributes.HasFlag(TextAttributes.Bold)).IsFalse();
    }

    // ----- GAUGE, SOUND ---------------------------------------------------------------

    [Test]
    public async Task AGaugeIsDeclaredOnASecureLineOnly()
    {
        var parser = new MxpParser();
        var gauges = new List<MxpGauge>();
        parser.GaugeDeclared += (_, e) => gauges.Add(e);

        var lines = Lines(parser, Secure + "<GAUGE hp MAX=maxhp CAPTION=Health COLOR=red>", "<GAUGE hp>");

        await Assert.That(gauges.Single()).IsEqualTo(new MxpGauge("hp", "maxhp", "Health", false));
        await Assert.That(lines[1]!.Text).IsEqualTo("<GAUGE hp>");
    }

    [Test]
    public async Task SoundAndMusicAreTheSameRequestsMspMakes()
    {
        var parser = new MxpParser();
        var media = new List<MediaCommand>();
        parser.MediaRequested += (_, e) => media.Add(e);

        Lines(parser, Secure + "<SOUND bell.wav V=40 P=80 U=\"https://m.example/\">", Secure + "<MUSIC theme.mid L=-1>", "<SOUND evil.wav>");

        var sound = (MediaCommand.Play)media[0];
        await Assert.That(sound.Request.Name).IsEqualTo("bell.wav");
        await Assert.That(sound.Request.Volume).IsEqualTo(40);
        await Assert.That(sound.Request.Priority).IsEqualTo(80);
        await Assert.That(sound.Request.Url).IsEqualTo("https://m.example/");
        var music = (MediaCommand.Play)media[1];
        await Assert.That(music.Request.Kind).IsEqualTo(MediaKind.Music);
        await Assert.That(music.Request.Loops).IsEqualTo(-1);
        await Assert.That(media.Count).IsEqualTo(2);
    }

    // ----- FRAME and DEST ---------------------------------------------------------------

    [Test]
    public async Task DestCutsItsTextOutOfTheLineAndSendsItToTheFrame()
    {
        var parser = new MxpParser();
        var sent = new List<MxpRedirect>();
        parser.Redirected += (_, e) => sent.Add(e);

        var lines = Lines(parser, Secure + "Main text <DEST Chat>Rivane: hi</DEST> more", Secure + "<DEST Chat>all of it</DEST>");

        await Assert.That(lines[0]!.Text).IsEqualTo("Main text  more");
        await Assert.That(lines[1]).IsNull();
        await Assert.That(parser.LineSwallowed).IsTrue();
        await Assert.That(sent.Select(s => (s.Frame, s.Line.Text))).IsEquivalentTo(
            new[] { ("Chat", "Rivane: hi"), ("Chat", "all of it") }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ARedirectSendsWholeLinesUntilTop()
    {
        var parser = new MxpParser();
        var sent = new List<MxpRedirect>();
        parser.Redirected += (_, e) => sent.Add(e);

        var lines = Lines(parser, Secure + "<FRAME Status REDIRECT>", "hp 10", "mp 5", Secure + "<FRAME _top REDIRECT>", "back");

        await Assert.That(sent.Select(s => s.Line.Text)).IsEquivalentTo(new[] { "hp 10", "mp 5" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(lines[4]!.Text).IsEqualTo("back");
    }

    [Test]
    public async Task OnlyAFewFramesMayBeNamedAndTheRestStayInTheMainWindow()
    {
        var parser = new MxpParser();
        var sent = new List<MxpRedirect>();
        parser.Redirected += (_, e) => sent.Add(e);

        var lines = Lines(parser, Enumerable.Range(0, MxpParser.MaxFrames + 2).Select(i => Secure + $"<DEST f{i}>t{i}</DEST>").ToArray());

        await Assert.That(sent.Count).IsEqualTo(MxpParser.MaxFrames);
        await Assert.That(lines[^1]!.Text).IsEqualTo($"t{MxpParser.MaxFrames + 1}");
    }

    [Test]
    public async Task DestOnAnOpenLineIsEchoed()
    {
        var parser = new MxpParser();
        var sent = new List<MxpRedirect>();
        parser.Redirected += (_, e) => sent.Add(e);

        var lines = Lines(parser, "<DEST Chat>forged</DEST>");

        await Assert.That(sent).IsEmpty();
        await Assert.That(lines[0]!.Text).IsEqualTo("<DEST Chat>forged</DEST>");
    }

    // ----- SUPPORTS -------------------------------------------------------------------------

    [Test]
    public async Task TheSupportsListNamesWhatIsHandled()
    {
        var parser = new MxpParser();
        string? reply = null;
        parser.ClientReply += (_, r) => reply = r;

        Lines(parser, Secure + "<SUPPORT>");

        foreach (var tag in new[] { "+!element", "+!entity", "+var", "+gauge", "+stat", "+sound", "+music", "+frame", "+dest" })
        {
            await Assert.That(reply!).Contains(tag);
        }

        await Assert.That(reply!).DoesNotContain("+high");
    }

    /// <summary>An MXP room source on a session nothing connects, so <c>Observe</c> can be driven directly.</summary>
    private sealed class MxpRoomSourceProbe : IDisposable
    {
        public MxpRoomSourceProbe()
        {
            Session = new SharpMUTerm.Core.Session.WorldSession(new SharpMUTerm.Core.Configuration.WorldDefinition { Name = "T", Host = "h", Port = 1 });
            Source = new MxpRoomSource(Session);
        }

        public SharpMUTerm.Core.Session.WorldSession Session { get; }

        public MxpRoomSource Source { get; }

        public void Dispose()
        {
            Source.Dispose();
            _ = Session.DisposeAsync();
        }
    }
}
