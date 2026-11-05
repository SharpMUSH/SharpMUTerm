namespace SharpMUTerm.Scripting.Tests;

public class ConsoleTests
{
    [Test]
    public async Task AnExpressionComesBackAsItsValue()
    {
        using var host = new ScriptHost(new FakeScriptWorld());
        var result = host.EvaluateConsole("1 + 2");
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Value).IsEqualTo("3");
    }

    [Test]
    public async Task AStatementRunsAndReturnsNothing()
    {
        var world = new FakeScriptWorld();
        using var host = new ScriptHost(world);
        var result = host.EvaluateConsole("x = 4; print('x is', x)");
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Value).IsNull();
        await Assert.That(world.Printed).Contains("x is\t4");
        await Assert.That(host.EvaluateConsole("x * 2").Value).IsEqualTo("8");
    }

    [Test]
    public async Task StringsAndTablesAreShownReadably()
    {
        using var host = new ScriptHost(new FakeScriptWorld());
        await Assert.That(host.EvaluateConsole("'hi'").Value).IsEqualTo("\"hi\"");
        await Assert.That(host.EvaluateConsole("{a = 1}").Value).IsEqualTo("{ a = 1 }");
        await Assert.That(host.EvaluateConsole("1, 'two'").Value).IsEqualTo("1\t\"two\"");
    }

    [Test]
    public async Task AnErrorIsReturnedWithItsLine()
    {
        using var host = new ScriptHost(new FakeScriptWorld());
        var result = host.EvaluateConsole("error('boom')");
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Error!).Contains("boom");
        await Assert.That(result.Error!).Contains("console");
    }

    [Test]
    public async Task ALoopThatNeverEndsIsStopped()
    {
        using var host = new ScriptHost(new FakeScriptWorld());
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = host.EvaluateConsole("while true do end", instructionLimit: 200_000);
        await Assert.That(result.Error!).Contains("stopped after");
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));

        // The host is still usable afterwards.
        await Assert.That(host.EvaluateConsole("2 * 21").Value).IsEqualTo("42");
    }

    [Test]
    public async Task ALoopIsStoppedByTimeToo()
    {
        using var host = new ScriptHost(new FakeScriptWorld());
        var result = host.EvaluateConsole(
            "local n = 0 while true do n = n + 1 end", instructionLimit: long.MaxValue, timeLimit: TimeSpan.FromMilliseconds(50));
        await Assert.That(result.Error!).Contains("ms");
    }

    [Test]
    public async Task AFilesErrorNamesTheFileAndLine()
    {
        var dir = Directory.CreateTempSubdirectory("smt-console-");
        try
        {
            var path = Path.Combine(dir.FullName, "broken.lua");
            File.WriteAllText(path, "local a = 1\nlocal b = nil\nreturn b.c\n");
            using var host = new ScriptHost(new FakeScriptWorld());
            var ex = Assert.Throws<ScriptException>(() => host.LoadFile(path));
            await Assert.That(ex!.Message).Contains("broken.lua");
            await Assert.That(ex.Line).IsEqualTo(3);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
