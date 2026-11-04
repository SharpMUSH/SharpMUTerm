using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Scripting.Tests;

/// <summary>
/// The script editing loop's file half: names are held to the scripts folder (the same rule a trigger set's file is), a
/// save is atomic and lands only under the root, and a reload after a save reports the error with its file and line.
/// </summary>
public class ScriptFilesTests
{
    private sealed class Root : IDisposable
    {
        public Root() => Path = Directory.CreateTempSubdirectory("sharpmuterm-scriptfiles-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Test]
    [Arguments("../escape.lua")]
    [Arguments("a/../../escape.lua")]
    [Arguments("/etc/passwd.lua")]
    [Arguments("C:/x.lua")]
    [Arguments("a\\b.lua")]
    [Arguments("./a.lua")]
    [Arguments("a//b.lua")]
    [Arguments("notes.txt")]
    [Arguments("")]
    [Arguments("bell\u0007.lua")]
    public async Task ANameThatCouldLeaveTheFolderIsRefused(string name)
    {
        using var root = new Root();

        await Assert.That(ScriptFiles.Check(name)).IsNotNull();
        await Assert.That(ScriptFiles.PathOf(root.Path, name).Path).IsNull();
        await Assert.That(ScriptFiles.Save(root.Path, name, "x = 1")).IsNotNull();
        await Assert.That(Directory.EnumerateFileSystemEntries(root.Path).Any()).IsFalse();
    }

    [Test]
    public async Task ANameIsGivenItsExtension()
    {
        await Assert.That(ScriptFiles.Normalise(" combat ")).IsEqualTo("combat.lua");
        await Assert.That(ScriptFiles.Normalise("lib/util.lua")).IsEqualTo("lib/util.lua");
        await Assert.That(ScriptFiles.Check("lib/util.lua")).IsNull();
    }

    [Test]
    public async Task ASaveWritesTheFileUnderTheRootAndLeavesNoTemporaryFile()
    {
        using var root = new Root();

        var error = ScriptFiles.Save(root.Path, "lib/util.lua", "x = 1\r\ny = 2");

        await Assert.That(error).IsNull();
        await Assert.That(File.ReadAllText(Path.Combine(root.Path, "lib", "util.lua"))).IsEqualTo("x = 1\ny = 2\n");
        await Assert.That(Directory.EnumerateFiles(root.Path, "*", SearchOption.AllDirectories).Count()).IsEqualTo(1);
        await Assert.That(ScriptFiles.List(root.Path).ToArray()).IsEquivalentTo(new[] { "lib/util.lua" });
        await Assert.That(ScriptFiles.Read(root.Path, "lib/util.lua").Text).IsEqualTo("x = 1\ny = 2\n");
    }

    [Test]
    public async Task ASaveReplacesTheWholeFile()
    {
        using var root = new Root();
        ScriptFiles.Save(root.Path, "a.lua", new string('-', 4_000));

        ScriptFiles.Save(root.Path, "a.lua", "short = true");

        await Assert.That(File.ReadAllText(Path.Combine(root.Path, "a.lua"))).IsEqualTo("short = true\n");
    }

    [Test]
    public async Task ANewScriptDoesNotOverwriteOne()
    {
        using var root = new Root();
        ScriptFiles.Save(root.Path, "a.lua", "kept = true");

        var error = ScriptFiles.Save(root.Path, "a.lua", "lost = true", mustBeNew: true);

        await Assert.That(error).IsNotNull();
        await Assert.That(File.ReadAllText(Path.Combine(root.Path, "a.lua"))).IsEqualTo("kept = true\n");
    }

    [Test]
    public async Task ASaveThroughALinkOutOfTheFolderIsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new Root();
        using var outside = new Root();
        Directory.CreateSymbolicLink(Path.Combine(root.Path, "away"), outside.Path);

        var error = ScriptFiles.Save(root.Path, "away/x.lua", "x = 1");

        await Assert.That(error).IsNotNull();
        await Assert.That(Directory.EnumerateFiles(outside.Path).Any()).IsFalse();
    }

    [Test]
    public async Task ASaveIntoAFolderThatIsNotThereIsRefused()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"sharpmuterm-none-{Guid.NewGuid():N}");

        await Assert.That(ScriptFiles.Save(missing, "a.lua", "x = 1")).IsNotNull();
        await Assert.That(Directory.Exists(missing)).IsFalse();
    }

    [Test]
    public async Task AReloadAfterASaveReportsTheErrorWithItsFileAndLine()
    {
        using var root = new Root();
        ScriptFiles.Save(root.Path, "main.lua", "function ok() return 1 end");
        var set = new TriggerSet { Name = "s", ScriptFiles = { "main.lua" } };
        var session = new WorldSession(
            new WorldDefinition { Name = "T", Host = "h", Port = 1 },
            triggerSets: new[] { set },
            sessionFactory: _ => new FakeTelnetSession());
        var reports = new List<string>();
        using var scripts = new SessionScripts(session, root.Path, reports.Add);
        scripts.Reload();
        await Assert.That(reports.Count).IsEqualTo(0);
        await Assert.That(scripts.Defines("ok")).IsTrue();

        ScriptFiles.Save(root.Path, "main.lua", "function ok() return 1 end\n\nfunction broken(\n");
        scripts.Reload();

        await Assert.That(reports.Count).IsGreaterThan(0);
        await Assert.That(reports[0]).Contains("main.lua");
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(reports[0], @"main\.lua:\(?\d+")).IsTrue();
    }
}
