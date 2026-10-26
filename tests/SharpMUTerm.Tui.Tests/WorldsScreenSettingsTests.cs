using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The settings that only <c>config.json</c> could change until F5 drew them: a world's content format and
/// emoji switches, and a character's on-disconnect commands. Each is edited through the screen's own field
/// and read back from the saved form.
/// </summary>
public class WorldsScreenSettingsTests
{
    private static List<WorldDefinition> Worlds() =>
    [
        new()
        {
            Name = "Convergence",
            Host = "convergence.example",
            Port = 4201,
            Characters = { new CharacterDefinition { Name = "Ann" } },
        },
    ];

    private static ScreenField WorldField(List<WorldDefinition> worlds, int field) =>
        WorldsScreenRenderer.Model(worlds, [], 0, 0).FieldAt(WorldsScreenRenderer.WorldsPane, 0, field)!.Value;

    private static WorldDefinition RoundTrip(List<WorldDefinition> worlds)
    {
        var config = new AppConfiguration();
        config.Worlds.AddRange(worlds);
        return ConfigurationStore.Deserialize(ConfigurationStore.Serialize(config)).Worlds[0];
    }

    [Test]
    [Arguments("auto", ContentFormat.Ansi)]
    [Arguments("ansi", ContentFormat.AnsiOnly)]
    [Arguments("MXP", ContentFormat.Mxp)]
    [Arguments("pueblo", ContentFormat.Pueblo)]
    public async Task ContentIsAChoiceOfFour(string typed, ContentFormat expected)
    {
        var worlds = Worlds();
        var field = WorldField(worlds, WorldsScreenRenderer.ContentField);

        await Assert.That(field.Get()).IsEqualTo("auto");
        await Assert.That(new ScreenEdits().Apply(field, typed)).IsNull();

        await Assert.That(worlds[0].ContentFormat).IsEqualTo(expected);
        await Assert.That(RoundTrip(worlds).ContentFormat).IsEqualTo(expected);
        await Assert.That(new ScreenEdits().Apply(field, "html")).IsNotNull();
    }

    [Test]
    [Arguments("off", false, true, true)]
    [Arguments("emoticons", true, true, false)]
    [Arguments("shortcodes", true, false, true)]
    [Arguments("both", true, true, true)]
    public async Task EmojiIsOneFieldOverThreeSwitches(string typed, bool enabled, bool emoticons, bool shortcodes)
    {
        var worlds = Worlds();
        var field = WorldField(worlds, WorldsScreenRenderer.EmojiField);

        await Assert.That(field.Get()).IsEqualTo("off");
        await Assert.That(new ScreenEdits().Apply(field, typed)).IsNull();

        var saved = RoundTrip(worlds).Emoji;
        await Assert.That((saved.Enabled, saved.Emoticons, saved.Shortcodes)).IsEqualTo((enabled, emoticons, shortcodes));
        await Assert.That(field.Get()).IsEqualTo(typed);
    }

    [Test]
    public async Task OnDisconnectSitsUnderOnConnect()
    {
        var worlds = Worlds();
        var model = WorldsScreenRenderer.Model(worlds, [], 0, 0);
        var field = model.FieldAt(WorldsScreenRenderer.CharactersPane, 0, WorldsScreenRenderer.OnDisconnectField)!.Value;

        await Assert.That(field.Label).IsEqualTo("disconnect");
        await Assert.That(model.FieldAt(WorldsScreenRenderer.CharactersPane, 0, WorldsScreenRenderer.OnConnectField)!.Value.Label)
            .IsEqualTo("on connect");
        await Assert.That(new ScreenEdits().Apply(field, "QUIT")).IsNull();

        await Assert.That(RoundTrip(worlds).Characters[0].OnDisconnect).IsEqualTo("QUIT");
    }

    [Test]
    public async Task TheDetailColumnShowsAllThree()
    {
        var worlds = Worlds();
        worlds[0].ContentFormat = ContentFormat.Mxp;
        worlds[0].Emoji.Enabled = true;
        worlds[0].Characters[0].OnDisconnect = "QUIT";

        var text = string.Join("\n", WorldsScreenRenderer.DetailColumn(worlds, [], 0, 0, "#ffffff").Select(MarkupText.Plain));

        await Assert.That(text).Contains("content");
        await Assert.That(text).Contains("mxp");
        await Assert.That(text).Contains("both");

        var form = string.Join("\n", WorldsScreenRenderer.FormColumn(worlds[0].Characters[0], "#ffffff").Select(MarkupText.Plain));
        await Assert.That(form).Contains("disconnect  QUIT");
        await Assert.That(form).Contains("QUIT");
    }
}
