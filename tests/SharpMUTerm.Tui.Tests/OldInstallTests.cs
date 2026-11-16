namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Reading the old Inno Setup installer's uninstall entry, and where a shortcut or link handler into its folder should
/// point now. The paths are Windows paths read as text, so this runs anywhere.
/// </summary>
public class OldInstallTests
{
    private const string Old = @"C:\Users\p\AppData\Local\Programs\SharpMUTerm";
    private const string Current = @"C:\Users\p\AppData\Local\SharpMUTerm\current";

    private static OldInstall Install() => new(Old, Old + @"\unins000.exe", ForEveryone: false);

    [Test]
    public async Task AnEntryNamingAFolderThatIsThereIsAnOldInstall()
    {
        var found = OldInstall.From(Old + @"\", "\"" + Old + "\\unins000.exe\"", everyone: false, Current, _ => true, _ => true);

        await Assert.That(found).IsEqualTo(new OldInstall(Old, Old + @"\unins000.exe", false));
    }

    [Test]
    public async Task AGoneFolderOrTheCurrentInstallIsNot()
    {
        await Assert.That(OldInstall.From(Old, null, false, Current, _ => false, _ => true)).IsNull();
        await Assert.That(OldInstall.From(null, null, false, Current, _ => true, _ => true)).IsNull();
        await Assert.That(OldInstall.From(@"C:\Users\p\AppData\Local\SharpMUTerm", null, false, Current, _ => true, _ => true)).IsNull()
            .Because("a folder holding the running install is never the old one");
        await Assert.That(OldInstall.From(Current + @"\sub", null, false, Current, _ => true, _ => true)).IsNull();
    }

    [Test]
    public async Task AMissingUninstallerIsNoUninstaller()
    {
        var found = OldInstall.From(Old, Old + @"\unins000.exe", true, Current, _ => true, _ => false);

        await Assert.That(found?.Uninstaller).IsNull();
        await Assert.That(found?.ForEveryone).IsTrue();
    }

    [Test]
    public async Task EitherProgramMovesToTheSameProgramInCurrent()
    {
        await Assert.That(Install().Retarget(Old + @"\sharpmuterm-window.exe", Current)).IsEqualTo(Current + @"\sharpmuterm-window.exe");
        await Assert.That(Install().Retarget(Old.ToUpperInvariant() + @"\SHARPMUTERM.EXE", Current)).IsEqualTo(Current + @"\sharpmuterm.exe");
    }

    [Test]
    public async Task AnythingElseIsLeftAlone()
    {
        await Assert.That(Install().Retarget(Old + @"\unins000.exe", Current)).IsNull();
        await Assert.That(Install().Retarget(Current + @"\sharpmuterm-window.exe", Current)).IsNull();
        await Assert.That(Install().Retarget(Old + @"2\sharpmuterm-window.exe", Current)).IsNull()
            .Because("a folder whose name only starts with the old one's is another folder");
    }

    [Test]
    public async Task ALinkHandlerKeepsItsArguments()
    {
        var command = "\"" + Old + "\\sharpmuterm-window.exe\" --open-link \"%1\"";

        await Assert.That(Install().RetargetCommand(command, Current)).IsEqualTo("\"" + Current + "\\sharpmuterm-window.exe\" --open-link \"%1\"");
        await Assert.That(Install().RetargetCommand("\"C:\\Other\\telnet.exe\" %1", Current)).IsNull();
        await Assert.That(Install().RetargetCommand(null, Current)).IsNull();
    }

    [Test]
    public async Task NothingIsFoundOffWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            await Assert.That(OldInstall.Find(Current)).IsEmpty();
        }
    }
}
