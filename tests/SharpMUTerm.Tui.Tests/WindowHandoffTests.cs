namespace SharpMUTerm.Tui.Tests;

/// <summary><c>sharpmuterm --window</c> hands the rest of its arguments to the window executable.</summary>
public class WindowHandoffTests
{
    [Test]
    public async Task TheWindowGetsEveryArgumentButTheFlag()
    {
        await Assert.That(WindowHandoff.Arguments(["example.org", "--window", "4201", "--tls"]))
            .IsEquivalentTo(new[] { "example.org", "4201", "--tls" });
    }

    [Test]
    public async Task TheWindowBesideThisProgramWinsOverOneOnPath()
    {
        using var beside = new TempDir();
        using var onPath = new TempDir();
        File.WriteAllText(Path.Combine(beside.Path, "w"), "");
        File.WriteAllText(Path.Combine(onPath.Path, "w"), "");

        await Assert.That(WindowHandoff.Find(beside.Path, onPath.Path, "w")).IsEqualTo(Path.Combine(beside.Path, "w"));
    }

    [Test]
    public async Task PathIsSearchedWhenNothingIsBesideThisProgram()
    {
        using var empty = new TempDir();
        using var onPath = new TempDir();
        File.WriteAllText(Path.Combine(onPath.Path, "w"), "");
        var path = string.Join(Path.PathSeparator, "/nonexistent-dir", onPath.Path);

        await Assert.That(WindowHandoff.Find(empty.Path, path, "w")).IsEqualTo(Path.Combine(onPath.Path, "w"));
        await Assert.That(WindowHandoff.Find(empty.Path, null, "w")).IsNull();
    }

    [Test]
    public async Task AMissingWindowSaysWhereItLookedAndFails()
    {
        // The test host has no sharpmuterm-window beside it unless the window was built into the same folder.
        if (WindowHandoff.Find(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH"), WindowHandoff.ExecutableName) is not null)
        {
            return;
        }

        var error = new StringWriter();
        await Assert.That(WindowHandoff.Run(["--window"], error)).IsEqualTo(1);
        await Assert.That(error.ToString()).Contains(WindowHandoff.ExecutableName);
    }

    [Test]
    public async Task TheTerminalsHelpNamesTheFlag()
    {
        await Assert.That(Program.UsageText).Contains(WindowHandoff.Flag);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("handoff-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
