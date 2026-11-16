using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// Retiring the old Inno Setup install from the window's install hook, against a desktop held in memory: the report
/// was a desktop shortcut that kept starting the old client after Setup had installed the new one.
/// </summary>
public class OldInstallMigrationTests
{
    private const string Old = @"C:\Users\p\AppData\Local\Programs\SharpMUTerm";
    private const string Current = @"C:\Users\p\AppData\Local\SharpMUTerm\current";
    private const string Desktop = @"C:\Users\p\Desktop\SharpMUTerm.lnk";
    private const string Menu = @"C:\Users\p\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\SharpMUTerm.lnk";
    private const string Pin = @"C:\Users\p\AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\SharpMUTerm.lnk";

    private sealed class FakeDesktop : IOldInstallDesktop
    {
        public readonly Dictionary<string, ShortcutFile> Files = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string?> AppIds = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Links = [];
        public string? PathRecord { get; set; }
        public int Uninstalls;

        public IReadOnlyList<ShortcutFile> Shortcuts() => [.. Files.Values];

        public IReadOnlyList<string> OldInstallerShortcuts(OldInstall install) => [Menu, Desktop];

        public bool Exists(string file) => Files.ContainsKey(file);

        public void Save(ShortcutFile shortcut, string? appId)
        {
            Files[shortcut.File] = shortcut;
            AppIds[shortcut.File] = appId;
        }

        public string? LinkCommand(string scheme) => Links.GetValueOrDefault(scheme);

        public void SetLinkCommand(string scheme, string command, string icon) => Links[scheme] = command;

        // What Inno's uninstaller does: its own shortcuts by name, its link keys, and Software\SharpMUTerm with our record.
        public bool Uninstall(OldInstall install, TimeSpan timeout)
        {
            Uninstalls++;
            Files.Remove(Menu);
            Files.Remove(Desktop);
            Links.Clear();
            PathRecord = null;
            return true;
        }

        public void Add(string file, string target, string arguments = "") =>
            Files[file] = new ShortcutFile(file, target, arguments, Path.GetDirectoryName(target.Replace('\\', '/'))!.Replace('/', '\\'), "SharpMUTerm", target);
    }

    private static void Run(FakeDesktop desktop, bool everyone = false) =>
        OldInstallMigration.Run([new OldInstall(Old, Old + @"\unins000.exe", everyone)], Current, desktop, step => step());

    [Test]
    public async Task TheOldDesktopShortcutStaysAndStartsTheNewWindow()
    {
        var desktop = new FakeDesktop();
        desktop.Add(Desktop, Old + @"\sharpmuterm-window.exe");

        Run(desktop);

        await Assert.That(desktop.Uninstalls).IsEqualTo(1);
        await Assert.That(desktop.Files[Desktop].Target).IsEqualTo(Current + @"\sharpmuterm-window.exe");
        await Assert.That(desktop.Files[Desktop].WorkingDirectory).IsEqualTo(Current);
        await Assert.That(desktop.Files[Desktop].Icon).IsEqualTo(Current + @"\sharpmuterm-window.exe");
        await Assert.That(desktop.AppIds[Desktop]).IsEqualTo(WindowsAppId.Value);
    }

    [Test]
    public async Task APinIsRetargetedAndTheTerminalKeepsItsProgram()
    {
        var desktop = new FakeDesktop();
        desktop.Add(Pin, Old + @"\sharpmuterm-window.exe");
        desktop.Add(@"C:\Users\p\Desktop\Terminal.lnk", Old + @"\sharpmuterm.exe", "--plain");

        Run(desktop);

        await Assert.That(desktop.Files[Pin].Target).IsEqualTo(Current + @"\sharpmuterm-window.exe");
        var terminal = desktop.Files[@"C:\Users\p\Desktop\Terminal.lnk"];
        await Assert.That(terminal.Target).IsEqualTo(Current + @"\sharpmuterm.exe");
        await Assert.That(terminal.Arguments).IsEqualTo("--plain");
        await Assert.That(desktop.AppIds[@"C:\Users\p\Desktop\Terminal.lnk"]).IsNull();
    }

    [Test]
    public async Task VelopacksStartMenuEntryIsPutBackWhenTheUninstallerDeletesItByName()
    {
        var desktop = new FakeDesktop();
        desktop.Add(Menu, Current + @"\sharpmuterm-window.exe");

        Run(desktop);

        await Assert.That(desktop.Files.ContainsKey(Menu)).IsTrue();
        await Assert.That(desktop.Files[Menu].Target).IsEqualTo(Current + @"\sharpmuterm-window.exe");
        await Assert.That(desktop.AppIds[Menu]).IsEqualTo(WindowsAppId.Value);
    }

    [Test]
    public async Task AnUnrelatedShortcutIsNotTouched()
    {
        var desktop = new FakeDesktop();
        desktop.Add(@"C:\Users\p\Desktop\Notepad.lnk", @"C:\Windows\notepad.exe");

        Run(desktop);

        await Assert.That(desktop.AppIds.ContainsKey(@"C:\Users\p\Desktop\Notepad.lnk")).IsFalse();
    }

    [Test]
    public async Task ALinkHandlerChosenInTheOldInstallerStartsTheNewWindow()
    {
        var desktop = new FakeDesktop();
        desktop.Links["telnet"] = "\"" + Old + "\\sharpmuterm-window.exe\" --open-link \"%1\"";
        desktop.Links["telnets"] = "\"C:\\Other\\client.exe\" %1";

        Run(desktop);

        await Assert.That(desktop.Links["telnet"]).IsEqualTo("\"" + Current + "\\sharpmuterm-window.exe\" --open-link \"%1\"");
        await Assert.That(desktop.Links.ContainsKey("telnets")).IsFalse().Because("another program's handler is the uninstaller's to keep or drop");
    }

    [Test]
    public async Task OurPathRecordSurvivesTheUninstallerDeletingItsKey()
    {
        var desktop = new FakeDesktop { PathRecord = Current };

        Run(desktop);

        await Assert.That(desktop.PathRecord).IsEqualTo(Current);
    }

    [Test]
    public async Task AnInstallForEveryoneIsNotUninstalledButItsShortcutsAreRetargeted()
    {
        var desktop = new FakeDesktop();
        desktop.Add(Desktop, Old + @"\sharpmuterm-window.exe");

        Run(desktop, everyone: true);

        await Assert.That(desktop.Uninstalls).IsEqualTo(0);
        await Assert.That(desktop.Files[Desktop].Target).IsEqualTo(Current + @"\sharpmuterm-window.exe");
    }

    [Test]
    public async Task NoOldInstallChangesNothing()
    {
        var desktop = new FakeDesktop();
        desktop.Add(Menu, Current + @"\sharpmuterm-window.exe");

        OldInstallMigration.Run([], Current, desktop, step => step());

        await Assert.That(desktop.AppIds).IsEmpty();
    }
}
