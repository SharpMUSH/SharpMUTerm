using SharpMUTerm.Client.Composer;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Panels;

namespace SharpMUTerm.Client.Tests;

/// <summary>The words the window's panels and welcome page add over the models both hosts already draw.</summary>
public class PanelHeadingTests
{
    [Test]
    public async Task WhoHereIsHeadedByTheRoomAndHowManyAreInIt()
    {
        var rows = new[]
        {
            new WhoHereRow("Ann", "Ann", WhoPresence.Active, null, false),
            new WhoHereRow("Bo", "Bo", WhoPresence.Idle, "5m", false),
            new WhoHereRow("guard", "a guard", WhoPresence.Active, null, true),
        };

        await Assert.That(PanelHeading.For(new WhoHereModel(true, "The Grand Plaza", rows))).IsEqualTo(new PanelHeading("Here", "The Grand Plaza · 3"));
        await Assert.That(PanelHeading.For(new WhoHereModel(true, null, rows))).IsEqualTo(new PanelHeading("Here", "3"));
        await Assert.That(PanelHeading.For(new WhoHereModel(false, null, []))).IsEqualTo(new PanelHeading("Here", null));
        await Assert.That(WhoHereModel.StateText(rows[0])).IsNull();
        await Assert.That(WhoHereModel.StateText(rows[1])).IsEqualTo("5m");
        await Assert.That(WhoHereModel.StateText(rows[2])).IsEqualTo("NPC");
    }

    [Test]
    public async Task VitalsNameWhereTheirNumbersComeFromOnlyWhenItIsOnePlace()
    {
        await Assert.That(VitalsModel.SourceOf(["Char.Vitals.hp", "Char.Vitals.maxhp"])).IsEqualTo("GMCP Char.Vitals");
        await Assert.That(VitalsModel.SourceOf(["Char.Vitals.hp", "Char.Status.level"])).IsEqualTo("GMCP");
        await Assert.That(VitalsModel.SourceOf(["MSDP.HEALTH", "MSDP.MANA"])).IsEqualTo("MSDP");
        await Assert.That(VitalsModel.SourceOf(["MSDP.HEALTH", "Char.Vitals.hp"])).IsNull();
        await Assert.That(VitalsModel.SourceOf([])).IsNull();
        var model = new VitalsModel([new VitalsMeter("hp", "10", "20", true, 0.5, false)], null) { Source = "MSDP" };
        await Assert.That(PanelHeading.For(model)).IsEqualTo(new PanelHeading("Vitals", "MSDP"));
        await Assert.That(model).IsNotEqualTo(model with { Source = "GMCP" }).Because("the source is drawn, so it is part of the model");
    }

    [Test]
    public async Task TheComposersFooterCountsParagraphsAndWords()
    {
        await Assert.That(ComposerModel.Summary("+pose waves.\nAnd sits.\n\nThen leaves.")).IsEqualTo("2 paragraphs · 6 words");
        await Assert.That(ComposerModel.Summary("hello")).IsEqualTo("1 paragraph · 1 word");
        await Assert.That(ComposerModel.Summary("  \n ")).IsEmpty();
    }

    [Test]
    public async Task TheWelcomesConnectLineIsTheCommandTheFormRunsThrough()
    {
        await Assert.That(WelcomeModel.CommandFor("", "", false)).IsEqualTo($"/connect {WelcomeModel.HostPlaceholder} 4000");
        await Assert.That(WelcomeModel.CommandFor(" mud.example.org ", "4201", true)).IsEqualTo("/connect mud.example.org 4201 tls");
        await Assert.That(WelcomeModel.CommandFor("::1", "23", false)).IsEqualTo("/connect [::1] 23");
    }
}
