using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Scripting.Tests;

/// <summary>
/// A session's Lua, end to end: files named by a trigger set, loaded from under the scripts folder, called by
/// the rules that name their functions. Until this existed no <see cref="ScriptHost"/> was ever built outside
/// these tests, so a configured <c>script</c> field did nothing.
/// </summary>
public class SessionScriptsTests
{
    private sealed class Fixture : IDisposable
    {
        public Fixture(string lua, Action<TriggerSet>? configure = null)
        {
            Root = Directory.CreateTempSubdirectory("sharpmuterm-scripts-").FullName;
            File.WriteAllText(Path.Combine(Root, "main.lua"), lua);
            Set = new TriggerSet { Name = "s", ScriptFiles = { "main.lua" } };
            configure?.Invoke(Set);
            Session = new WorldSession(
                new WorldDefinition { Name = "T", Host = "h", Port = 1 },
                triggerSets: new[] { Set },
                sessionFactory: _ => Telnet);
            Scripts = new SessionScripts(Session, Root, Reports.Add);
        }

        public string Root { get; }

        public TriggerSet Set { get; }

        public FakeTelnetSession Telnet { get; } = new();

        public WorldSession Session { get; }

        public SessionScripts Scripts { get; }

        public List<string> Reports { get; } = new();

        public void Dispose()
        {
            Scripts.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    [Test]
    public async Task AConfiguredTriggerCallsTheNamedFunctionWithItsCaptures()
    {
        using var f = new Fixture(
            "function on_tell(line, who, what) world.send('reply ' .. who .. ' ' .. what) end",
            set => set.Triggers.Add(new Trigger
            {
                Name = "tell",
                Pattern = @"^(\w+) tells you '(.*)'$",
                Actions = new TriggerActions { ScriptCallback = "on_tell" },
            }));
        await Assert.That(f.Scripts.Reload()).IsEqualTo(1);
        await f.Session.ConnectAsync();

        f.Telnet.EmitLine("Ann tells you 'hi'");

        await Assert.That(f.Telnet.SentLines).Contains("reply Ann hi");
        await Assert.That(f.Reports).IsEmpty();
    }

    [Test]
    public async Task ATableFunctionIsFoundByItsDottedName()
    {
        using var f = new Fixture(
            "combat = {} function combat.flee() world.send('flee') end",
            set => set.Macros.Add(new Macro { Key = "F12", ScriptCallback = "combat.flee" }));
        f.Scripts.Reload();
        await f.Session.ConnectAsync();

        await f.Session.HandleKeyAsync("F12");

        await Assert.That(f.Telnet.SentLines).IsEquivalentTo(new[] { "flee" });
        await Assert.That(f.Scripts.Defines("combat.flee")).IsTrue();
        await Assert.That(f.Scripts.Defines("combat.fight")).IsFalse();
    }

    /// <summary>A rule naming a function nobody wrote says so — once, not on every line it matches.</summary>
    [Test]
    public async Task AMissingFunctionIsReportedOnce()
    {
        using var f = new Fixture(
            "-- nothing here",
            set => set.Triggers.Add(new Trigger
            {
                Name = "any",
                Pattern = "^x",
                Actions = new TriggerActions { ScriptCallback = "nope" },
            }));
        f.Scripts.Reload();
        await f.Session.ConnectAsync();

        f.Telnet.EmitLine("x1");
        f.Telnet.EmitLine("x2");

        await Assert.That(f.Reports).HasSingleItem();
        await Assert.That(f.Reports[0]).Contains("nope");
    }

    /// <summary>Re-running a file over its own registrations would fire every <c>trigger.add</c> rule twice.</summary>
    [Test]
    public async Task ReloadingDoesNotDoubleWhatTheScriptRegistered()
    {
        using var f = new Fixture("trigger.add('^ping$', function() world.send('pong') end)");
        f.Scripts.Reload();
        f.Scripts.Reload();
        await f.Session.ConnectAsync();

        f.Telnet.EmitLine("ping");

        await Assert.That(f.Telnet.SentLines).IsEquivalentTo(new[] { "pong" });
        await Assert.That(f.Session.Triggers.Triggers).HasSingleItem();
    }

    [Test]
    public async Task SyncFilesRebuildsOnlyWhenTheListChanges()
    {
        using var f = new Fixture("count = (count or 0) + 1");
        f.Scripts.Reload();

        await Assert.That(f.Scripts.SyncFiles()).IsFalse();

        File.WriteAllText(Path.Combine(f.Root, "two.lua"), "x = 1");
        f.Set.ScriptFiles.Add("two.lua");
        f.Session.ReloadAutomation(new[] { f.Set });

        await Assert.That(f.Scripts.SyncFiles()).IsTrue();
        await Assert.That(f.Scripts.Files).IsEquivalentTo(new[] { "main.lua", "two.lua" });
    }

    /// <summary>
    /// A trigger set is shared configuration, and later a package can bring one. Neither may make the client
    /// read a file outside the scripts folder, by an absolute path or by climbing out of it.
    /// </summary>
    [Test]
    [Arguments("../outside.lua")]
    [Arguments("sub/../../outside.lua")]
    [Arguments("/etc/passwd")]
    public async Task APathOutsideTheRootIsRefusedAndReported(string file)
    {
        using var f = new Fixture("x = 1", set => set.ScriptFiles.Add(file));

        var loaded = f.Scripts.Reload();

        await Assert.That(loaded).IsEqualTo(1);
        await Assert.That(f.Reports).HasSingleItem();
        await Assert.That(f.Reports[0]).Contains("outside the scripts folder");
    }

    [Test]
    public async Task ResolveKeepsNamesInsideTheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "root");

        await Assert.That(SessionScripts.Resolve(root, "a.lua")).IsEqualTo(Path.Combine(root, "a.lua"));
        await Assert.That(SessionScripts.Resolve(root, "lib/b.lua")).IsEqualTo(Path.Combine(root, "lib", "b.lua"));
        await Assert.That(SessionScripts.Resolve(root, Path.Combine(root, "c.lua"))).IsEqualTo(Path.Combine(root, "c.lua"));
        await Assert.That(SessionScripts.Resolve(root, "../rootkit/d.lua")).IsNull();
        await Assert.That(SessionScripts.Resolve(root, "")).IsNull();
    }

    [Test]
    public async Task AFileThatFailsToLoadIsReportedAndTheRestStillLoad()
    {
        using var f = new Fixture("function ok() end", set => set.ScriptFiles.Insert(0, "missing.lua"));

        var loaded = f.Scripts.Reload();

        await Assert.That(loaded).IsEqualTo(1);
        await Assert.That(f.Reports).HasSingleItem();
        await Assert.That(f.Reports[0]).Contains("missing.lua");
        await Assert.That(f.Scripts.Defines("ok")).IsTrue();
    }

    /// <summary>The sandbox holds for a file loaded this way exactly as for inline code.</summary>
    [Test]
    public async Task ALoadedFileIsSandboxed()
    {
        using var f = new Fixture("os.execute('true')");

        f.Scripts.Reload();

        await Assert.That(f.Reports).HasSingleItem();
    }

    [Test]
    public async Task APackageScriptLoadsThroughThePackageResolverAndNowhereElse()
    {
        var root = Directory.CreateTempSubdirectory("sharpmuterm-pkgscript-").FullName;
        try
        {
            var lua = Path.Combine(root, "status.lua");
            File.WriteAllText(lua, "function pkg_hello() world.send('hello') end");
            var set = new TriggerSet { Name = "pkg", ScriptFiles = { "package:demo/status.lua", "package:gone/x.lua" } };
            var telnet = new FakeTelnetSession();
            var session = new WorldSession(
                new WorldDefinition { Name = "T", Host = "h", Port = 1 }, triggerSets: new[] { set }, sessionFactory: _ => telnet);
            var reports = new List<string>();
            using var scripts = new SessionScripts(
                session, root, reports.Add, name => name == "package:demo/status.lua" ? lua : null);

            await Assert.That(scripts.Reload()).IsEqualTo(1);
            await Assert.That(scripts.Defines("pkg_hello")).IsTrue();
            await Assert.That(reports.Single()).Contains("not in an installed package");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
