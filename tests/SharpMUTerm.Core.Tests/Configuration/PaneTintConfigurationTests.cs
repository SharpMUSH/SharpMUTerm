using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Tests.Configuration;

/// <summary>
/// <see cref="CharacterDefinition.Tint"/> on disk: the colour a character's panes are painted in.
/// <para>
/// The interesting claim is the one about <em>absence</em>. This is a new optional field whose default is
/// the behaviour the configuration already had, so no migration step marks anybody and the schema version
/// does not move — the opposite of <see cref="EncodingMigrationTests"/>, where the same stored value
/// started meaning something else and a rewrite was the only honest answer. What has to be pinned is
/// therefore that every document written before the field existed still loads, and loads
/// <em>untinted</em>: a migration that assigned colours — or a default that was anything but
/// <see cref="PaneTint.None"/> — would repaint the workspace of every user who never asked for one.
/// </para>
/// </summary>
public class PaneTintConfigurationTests
{
    /// <summary>A current-schema document from before the field: characters, and no <c>tint</c> anywhere.</summary>
    private const string DocumentWithoutTheField = """
        {
          "version": 5,
          "worlds": [
            {
              "name": "Aetherfall",
              "host": "aetherfall.mux",
              "port": 4201,
              "characters": [
                { "name": "Corvid", "connectAtStartup": true },
                { "name": "Rookery" }
              ]
            }
          ],
          "triggerSets": []
        }
        """;

    /// <summary>And a v1 document, so the answer is the same after every migration step has run over it.</summary>
    private const string V1Document = """
        {
          "version": 1,
          "worlds": [
            { "name": "Aetherfall", "host": "aetherfall.mux", "port": 4201 }
          ]
        }
        """;

    [Test]
    public async Task ANewCharacterIsUntinted()
    {
        await Assert.That(new CharacterDefinition().Tint).IsEqualTo(PaneTint.None);
    }

    [Test]
    public async Task ADocumentWrittenBeforeTheFieldLoadsUntinted()
    {
        var config = ConfigurationStore.Deserialize(DocumentWithoutTheField);

        await Assert.That(config.Worlds[0].Characters.Select(c => c.Tint))
            .IsEquivalentTo(new[] { PaneTint.None, PaneTint.None });

        // The rest of the character survived the load — this is a field being absent, not a document
        // being misread.
        await Assert.That(config.Worlds[0].Characters[0].ConnectAtStartup).IsTrue();
    }

    [Test]
    public async Task AV1DocumentComesThroughEveryMigrationStepUntinted()
    {
        var config = ConfigurationStore.Deserialize(V1Document);

        await Assert.That(config.Version).IsEqualTo(AppConfiguration.CurrentVersion);

        // v1→v2 invents a character for the world; it must arrive with no colour on it either.
        await Assert.That(config.Worlds[0].Characters.Count).IsEqualTo(1);
        await Assert.That(config.Worlds[0].Characters[0].Tint).IsEqualTo(PaneTint.None);
    }

    /// <summary>
    /// A round trip through the real serializer. Asserted on the <em>text</em> as well as on the value,
    /// because <c>config.json</c> is a file people hand-edit and paste into bug reports: the shared
    /// options carry a string enum converter, so the field reads as <c>"Moss"</c> and not as the ordinal
    /// <c>3</c>, which would be both unreadable and a number that moves if the enum ever gains a member.
    /// </summary>
    [Test]
    public async Task ATintRoundTripsByName()
    {
        var config = new AppConfiguration();
        config.Worlds.Add(new WorldDefinition
        {
            Name = "Aetherfall",
            Host = "aetherfall.mux",
            Port = 4201,
            Characters =
            {
                new CharacterDefinition { Name = "Corvid", Tint = PaneTint.Moss },
                new CharacterDefinition { Name = "Rookery" },
            },
        });

        var json = ConfigurationStore.Serialize(config);
        await Assert.That(json).Contains("\"tint\": \"Moss\"");

        var loaded = ConfigurationStore.Deserialize(json);
        await Assert.That(loaded.Worlds[0].Characters[0].Tint).IsEqualTo(PaneTint.Moss);
        await Assert.That(loaded.Worlds[0].Characters[1].Tint).IsEqualTo(PaneTint.None);
    }

    /// <summary>
    /// Every member survives the round trip, so a colour added to the enum later cannot be one that only
    /// looked like it worked. Cheap, and it is the whole vocabulary the F5 row offers.
    /// </summary>
    [Test]
    public async Task EveryTintRoundTrips()
    {
        foreach (var tint in Enum.GetValues<PaneTint>())
        {
            var config = new AppConfiguration();
            config.Worlds.Add(new WorldDefinition
            {
                Name = "W",
                Characters = { new CharacterDefinition { Name = "C", Tint = tint } },
            });

            var loaded = ConfigurationStore.Deserialize(ConfigurationStore.Serialize(config));
            await Assert.That(loaded.Worlds[0].Characters[0].Tint).IsEqualTo(tint);
        }
    }
}
