using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Media;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Tests.Session;

namespace SharpMUTerm.Core.Tests.Media;

public class MspTriggerTests
{
    private static MediaRequest Played(string line) =>
        ((MediaCommand.Play)MspTrigger.Parse(line)!).Request;

    [Test]
    public async Task ASoundWithEveryField()
    {
        var sound = Played("!!SOUND(weather/rain.wav V=80 L=2 P=70 T=weather U=https://example.org/sounds/)");

        await Assert.That(sound.Name).IsEqualTo("weather/rain.wav");
        await Assert.That(sound.Kind).IsEqualTo(MediaKind.Sound);
        await Assert.That(sound.Volume).IsEqualTo(80);
        await Assert.That(sound.Loops).IsEqualTo(2);
        await Assert.That(sound.Priority).IsEqualTo(70);
        await Assert.That(sound.Tag).IsEqualTo("weather");
        await Assert.That(sound.Url).IsEqualTo("https://example.org/sounds/");
        await Assert.That(sound.Exclusive).IsTrue();
    }

    [Test]
    public async Task MspsDefaultsAreFilledIn()
    {
        var sound = Played("!!SOUND(bell.wav)");
        var music = Played("!!MUSIC(town.mid L=-1)");

        await Assert.That(sound.Volume).IsEqualTo(100);
        await Assert.That(sound.Loops).IsEqualTo(1);
        await Assert.That(sound.Priority).IsEqualTo(50);
        await Assert.That(music.Kind).IsEqualTo(MediaKind.Music);
        await Assert.That(music.Loops).IsEqualTo(-1);
        await Assert.That(music.Continue).IsTrue();
        await Assert.That(music.Key).IsEqualTo(MspTrigger.MusicKey);
        await Assert.That(Played("!!MUSIC(town.mid C=0)").Continue).IsFalse();
    }

    [Test]
    public async Task OffStopsThatKindAndCanSetTheFolder()
    {
        var stopSound = (MediaCommand.Stop)MspTrigger.Parse("!!SOUND(Off)")!;
        var stopMusic = (MediaCommand.Stop)MspTrigger.Parse("!!MUSIC(Off)")!;
        var folder = (MediaCommand.SetDefault)MspTrigger.Parse("!!SOUND(Off U=https://example.org/s/)")!;

        await Assert.That(stopSound.Filter.Kind).IsEqualTo(MediaKind.Sound);
        await Assert.That(stopMusic.Filter.Kind).IsEqualTo(MediaKind.Music);
        await Assert.That(folder.Url).IsEqualTo("https://example.org/s/");
    }

    [Test]
    [Arguments("You hear !!SOUND(bell.wav)")]
    [Arguments("!!SOUND(bell.wav) rings")]
    [Arguments("!!SOUND()")]
    [Arguments("!!SOUND(bell.wav V)")]
    [Arguments("!!SOUND(bell.wav")]
    [Arguments("!!NOISE(bell.wav)")]
    public async Task AnythingButAWholeTriggerIsText(string line)
    {
        await Assert.That(MspTrigger.Parse(line)).IsNull();
    }

    [Test]
    public async Task ATriggerIsTextUntilMspIsAgreed()
    {
        var telnet = new FakeTelnetSession();
        await using var session = new WorldSession(
            new WorldDefinition { Name = "W", Host = "w.example", Port = 1 }, sessionFactory: _ => telnet);
        var heard = new List<MediaCommand>();
        session.MediaReceived += (_, c) => heard.Add(c);
        await session.ConnectAsync();

        telnet.EmitLine("!!SOUND(bell.wav)");
        telnet.RaiseMsp(true);
        telnet.EmitLine("!!SOUND(gong.wav)");
        telnet.RaiseMsp(false);
        telnet.EmitLine("!!SOUND(drum.wav)");

        var lines = session.Scrollback.Snapshot().Select(l => l.Text).ToList();
        await Assert.That(lines).Contains("!!SOUND(bell.wav)");
        await Assert.That(lines).DoesNotContain("!!SOUND(gong.wav)");
        await Assert.That(lines).Contains("!!SOUND(drum.wav)");
        await Assert.That(heard.Count).IsEqualTo(1);
        await Assert.That(((MediaCommand.Play)heard[0]).Request.Name).IsEqualTo("gong.wav");
    }
}
