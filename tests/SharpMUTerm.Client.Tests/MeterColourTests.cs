using SharpMUTerm.Client.Panels;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Tests;

/// <summary>A meter's colour: the defaults by label, how a colour is written and read, and the form's round trip.</summary>
public class MeterColourTests
{
    [Test]
    public async Task AMeterWithNoColourOfItsOwnTakesItsLabelsDefault()
    {
        await Assert.That(MeterColours.For(new StatusMeter { Label = "hp" })).IsEqualTo("#4cc38a");
        await Assert.That(MeterColours.For(new StatusMeter { Label = "Mana" })).IsEqualTo("#5aa9e6");
        await Assert.That(MeterColours.For(new StatusMeter { Label = "mv" })).IsEqualTo("#e8b84a");

        // A label no MUD shares takes the client's accent: no colour.
        await Assert.That(MeterColours.For(new StatusMeter { Label = "gold" })).IsNull();
        await Assert.That(MeterColours.Label(new StatusMeter { Label = "hp" })).IsEqualTo(MeterColours.Auto);
    }

    [Test]
    public async Task AMeterscolourIsAPaletteNameInItsMeterShadeOrAHexAsWritten()
    {
        await Assert.That(MeterColours.For(new StatusMeter { Label = "hp", Colour = "purple" })).IsEqualTo("#a98bf0");
        await Assert.That(MeterColours.For(new StatusMeter { Label = "gold", Colour = "#123456" })).IsEqualTo("#123456");
        await Assert.That(MeterColours.Hex(MeterColours.Auto)).IsNull();

        // Every name a picker offers has a shade of its own, never the web colour.
        foreach (var name in MeterColours.Choices.Where(n => n != MeterColours.Auto))
        {
            await Assert.That(MeterColours.Hex(name)).IsNotNull();
        }

        await Assert.That(MeterColours.Hex("green")).IsNotEqualTo("#008000");
    }

    [Test]
    public async Task ATypedColourIsStoredNormalisedAndNonsenseIsRefused()
    {
        await Assert.That(MeterColours.TryNormalise("Lime", out var lime)).IsTrue();
        await Assert.That(lime).IsEqualTo("lime");
        await Assert.That(MeterColours.TryNormalise("auto", out var auto)).IsTrue();
        await Assert.That(auto).IsNull();
        await Assert.That(MeterColours.TryNormalise("#ff00ff", out var hex)).IsTrue();
        await Assert.That(hex).IsEqualTo("#ff00ff");
        await Assert.That(MeterColours.TryNormalise("sparkly", out _)).IsFalse();
    }

    [Test]
    public async Task TheOneLineFormCarriesAColourAndAMeterWithoutOneReadsAsBefore()
    {
        var meters = StatusMeter.Parse("hp=Char.Vitals.hp/Char.Vitals.maxhp @lime; gold=Char.Status.gold");
        await Assert.That(meters[0].Max).IsEqualTo("Char.Vitals.maxhp");
        await Assert.That(meters[0].Colour).IsEqualTo("lime");
        await Assert.That(meters[1].Colour).IsNull();
        await Assert.That(StatusMeter.Format(meters)).IsEqualTo("hp=Char.Vitals.hp/Char.Vitals.maxhp @lime; gold=Char.Status.gold");

        // The defaults carry no colour, so a configuration from before the field is still at its default.
        await Assert.That(StatusMeter.Defaults().All(m => m.Colour is null)).IsTrue();
    }

    [Test]
    public async Task AColourIsAChangeTheResetSeesAndTheFormStoresItThroughTheFunnel()
    {
        var config = new Core.Configuration.AppConfiguration();
        var field = BuiltInSettings.Text.Fields.OfType<Contributions.ListField>().Single(f => f.Id == BuiltInSettings.MetersField);
        var binding = (SettingBinding<List<StatusMeter>>)field.Binding!;
        await Assert.That(binding.IsDefault(config)).IsTrue();

        var form = new SettingsForm(BuiltInSettings.All, config, change => change(), _ => { }, "text", isDark: true);
        var meters = form.Meters(field);
        meters[0].Colour = "Purple";
        form.SetMeters(field, meters);

        await Assert.That(config.Text.StatusMeters[0].Colour).IsEqualTo("purple");
        await Assert.That(binding.IsDefault(config)).IsFalse();
        await Assert.That(form.Meters(field)[0].Colour).IsEqualTo("purple");
    }

    [Test]
    public async Task TheVitalsModelCarriesEachMetersColourAndTheTargets()
    {
        var a = new VitalsModel([new VitalsMeter("hp", "1", "2", true, 0.5, false, "#4cc38a")], new VitalsTarget("rat", 0.5));
        var b = new VitalsModel([new VitalsMeter("hp", "1", "2", true, 0.5, false, "#a98bf0")], new VitalsTarget("rat", 0.5));
        await Assert.That(a).IsNotEqualTo(b);
        await Assert.That(a.Target!.Colour).IsEqualTo(MeterColours.Target);
    }
}
