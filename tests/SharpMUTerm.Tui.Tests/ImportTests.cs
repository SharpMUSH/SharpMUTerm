using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Import from another client (<c>/import</c>, ⌃P ▸ Import from another client…, the welcome's row): the review opens
/// before anything is written, ⏎ writes, Esc writes nothing, and passwords are stored only when switched on.
/// </summary>
[NotInParallel]
public class ImportTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static ConsoleKeyInfo Key(ConsoleKey key, char c = '\0') => new(c, key, false, false, false);

    private static SharpMUTermApp App(AppConfiguration? config = null, IFilePicker? picker = null)
    {
        Console.SetIn(TextReader.Null);
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 40), null, null, null) with { FilePicker = picker };
        return new SharpMUTermApp(config ?? new AppConfiguration(), host);
    }

    private static void TypeCommand(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.SimulateKey(Key(ConsoleKey.Enter, '\r'));
    }

    private static string Fixture()
    {
        var path = Path.Combine(Path.GetTempPath(), "sharpmuterm-import-" + Guid.NewGuid().ToString("N") + ".mcl");
        File.WriteAllText(path, DemoImport.Mushclient);
        return path;
    }

    [Test]
    public async Task SlashImportOpensTheReview_AndNothingIsWrittenUntilEnter()
    {
        var path = Fixture();
        try
        {
            var app = App();
            TypeCommand(app, "/import " + path);

            await Assert.That(app.ImportReviewOpen).IsTrue();
            await Assert.That(app.Configuration.Worlds).IsEmpty();
            await Assert.That(app.Configuration.TriggerSets).IsEmpty();
            var shown = string.Join("\n", app.ImportReviewLines);
            await Assert.That(shown).Contains("WHAT CAN ACT AS YOU");
            await Assert.That(shown).Contains("Northwind");
            await Assert.That(shown).Contains("LEFT OUT");

            app.SimulateImportKey(Key(ConsoleKey.Enter, '\r'));

            await Assert.That(app.ImportReviewOpen).IsFalse();
            var world = app.Configuration.Worlds.Single();
            await Assert.That(world.Host).IsEqualTo("northwind.example.org");
            var character = world.Characters.Single();
            await Assert.That(character.Name).IsEqualTo("Wren");
            await Assert.That(character.Password).IsNull().Because("the password was not switched on");
            await Assert.That(character.TriggerSets.Single()).IsEqualTo("Imported from MUSHclient");
            await Assert.That(app.Configuration.TriggerSets.Single().Triggers).IsNotEmpty();
            await Assert.That(app.LastNotice).Contains("Imported");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task PStoresThePasswords_AndEscWritesNothing()
    {
        var path = Fixture();
        try
        {
            var app = App();
            app.ImportFrom(path);
            app.SimulateImportKey(Key(ConsoleKey.Escape, '\u001b'));

            await Assert.That(app.ImportReviewOpen).IsFalse();
            await Assert.That(app.Configuration.Worlds).IsEmpty();
            await Assert.That(app.PendingImport).IsNull();

            app.ImportFrom(path);
            await Assert.That(app.PendingImport!.StorePasswords).IsFalse();
            app.SimulateImportKey(Key(ConsoleKey.P, 'p'));
            await Assert.That(app.PendingImport!.StorePasswords).IsTrue();
            await Assert.That(string.Join("\n", app.ImportReviewLines)).Contains("will be stored");
            app.SimulateImportKey(Key(ConsoleKey.Enter, '\r'));

            await Assert.That(app.Configuration.Worlds.Single().Characters.Single().Password).IsEqualTo("not-a-real-one");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ImportingTwiceAddsASecondSet_AndNoSecondWorld()
    {
        var path = Fixture();
        try
        {
            var app = App();
            app.ImportFrom(path);
            app.SimulateImportKey(Key(ConsoleKey.Enter, '\r'));
            app.ImportFrom(path);

            await Assert.That(app.PendingImport!.Adds[0].Detail).Contains("already configured");
            app.SimulateImportKey(Key(ConsoleKey.Enter, '\r'));

            await Assert.That(app.Configuration.Worlds.Count).IsEqualTo(1);
            await Assert.That(app.Configuration.TriggerSets.Select(s => s.Name).ToArray())
                .IsEquivalentTo(new[] { "Imported from MUSHclient", "Imported from MUSHclient (2)" });
            await Assert.That(app.Configuration.Worlds[0].Characters.Single().TriggerSets.Single()).IsEqualTo("Imported from MUSHclient");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task AFileThatIsNoSetupIsRefusedOutLoud()
    {
        var app = App();
        TypeCommand(app, "/import /nonexistent/nothing-here.mcl");

        await Assert.That(app.ImportReviewOpen).IsFalse();
        await Assert.That(app.LastNotice).Contains("was not imported");
    }

    [Test]
    public async Task ThePaletteEntryAndBareSlashImportAskForAPath_WhereThereIsNoPicker()
    {
        var app = App();
        await Assert.That(app.DispatchCommand(BuiltInCommands.ImportCommand)).IsTrue();
        await Assert.That(app.PrimaryInputText).IsEqualTo("/import ");
        await Assert.That(app.LastNotice).Contains("Type the path");

        var other = App();
        TypeCommand(other, "/import");
        await Assert.That(other.PrimaryInputText).IsEqualTo("/import ");
    }

    [Test]
    public async Task AHostWithAPickerOpensTheReviewForThePickedFile()
    {
        var path = Fixture();
        try
        {
            var picker = new FakePicker(path);
            var app = App(picker: picker);
            app.DispatchCommand(BuiltInCommands.ImportCommand);

            await Assert.That(picker.Asked).IsEqualTo(1);
            await Assert.That(app.ImportReviewOpen).IsTrue();
            await Assert.That(app.PrimaryInputText).IsEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task TheWelcomesImportRowStartsAnImport()
    {
        var app = App();
        app.ShowWelcome();
        await Assert.That(string.Join("\n", app.WelcomeLines)).Contains("Import");
        for (var i = 0; i < 3; i++)
        {
            app.SimulateWelcomeKey(Key(ConsoleKey.DownArrow));
        }

        await Assert.That(app.Welcome!.Selected).IsEqualTo(WelcomeChoice.Import);
        app.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(app.WelcomeIsOpen).IsFalse();
        await Assert.That(app.PrimaryInputText).IsEqualTo("/import ");
    }

    private sealed class FakePicker(string path) : IFilePicker
    {
        public int Asked { get; private set; }

        public bool Available => true;

        public void Pick(string title, Action<string?> picked)
        {
            Asked++;
            picked(path);
        }
    }
}
