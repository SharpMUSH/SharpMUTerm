using SharpMUTerm.Core.Media;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Tests.Session;

namespace SharpMUTerm.Core.Tests.Media;

public class MediaCommandTests
{
    [Test]
    public async Task APlayTakesTheSpecificationsDefaults()
    {
        var play = (MediaCommand.Play)MediaCommand.FromGmcp("Client.Media.Play", """{"name":"rain.ogg"}""")!;

        await Assert.That(play.Request.Volume).IsEqualTo(50);
        await Assert.That(play.Request.Loops).IsEqualTo(1);
        await Assert.That(play.Request.Kind).IsEqualTo(MediaKind.Sound);
        await Assert.That(play.Request.Continue).IsTrue();
        await Assert.That(play.Request.Priority).IsNull();
    }

    [Test]
    public async Task APlayReadsEveryField()
    {
        var play = (MediaCommand.Play)MediaCommand.FromGmcp(
            "Client.Media.Play",
            """{"name":"m/theme.mp3","type":"music","volume":200,"loops":-1,"priority":70,"continue":false,"key":"bgm","tag":"area","fadein":500,"caption":"a lute"}""")!;

        await Assert.That(play.Request.Kind).IsEqualTo(MediaKind.Music);
        await Assert.That(play.Request.Volume).IsEqualTo(100);
        await Assert.That(play.Request.Loops).IsEqualTo(-1);
        await Assert.That(play.Request.Priority).IsEqualTo(70);
        await Assert.That(play.Request.Continue).IsFalse();
        await Assert.That(play.Request.FadeIn).IsEqualTo(TimeSpan.FromMilliseconds(500));
        await Assert.That(play.Request.CaptionText).IsEqualTo("a lute");
    }

    [Test]
    public async Task VideoAndOtherPackagesAreNotMedia()
    {
        await Assert.That(MediaCommand.FromGmcp("Client.Media.Play", """{"name":"x.mp4","type":"video"}""")).IsNull();
        await Assert.That(MediaCommand.FromGmcp("Char.Vitals", "{}")).IsNull();
    }

    [Test]
    public async Task AnEmptyStopStopsEverything()
    {
        var stop = (MediaCommand.Stop)MediaCommand.FromGmcp("Client.Media.Stop", "")!;

        await Assert.That(stop.Filter).IsEqualTo(MediaFilter.All);
    }

    [Test]
    public async Task TheSessionRaisesMediaFromGmcp()
    {
        var telnet = new FakeTelnetSession();
        var session = new WorldSession(new WorldDefinition { Name = "T", Host = "h", Port = 1 }, sessionFactory: _ => telnet);
        MediaCommand? received = null;
        session.MediaReceived += (_, m) => received = m;
        await session.ConnectAsync();

        telnet.EmitGmcp("Client.Media.Default", """{"url":"https://x.example/s/"}""");

        await Assert.That(received).IsEqualTo(new MediaCommand.SetDefault("https://x.example/s/"));
    }
}
