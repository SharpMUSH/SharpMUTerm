using System.IO.Compression;
using System.Text;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Import;

namespace SharpMUTerm.Core.Tests.Import;

/// <summary>
/// The readers for other clients' setups (<see cref="MushclientImport"/>, <see cref="MudletImport"/>,
/// <see cref="TinTinImport"/>), over fixtures written here. What they map, what they leave out and say so, and the
/// bounds an untrusted file is held to.
/// </summary>
public class ClientImportTests
{
    private const string Mushclient = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE muclient>
        <muclient>
        <world name="Northwind" site="northwind.example.org" port="4201" player="Wren" password="s3cret" />
        <triggers>
          <trigger enabled="y" match="* pages: *" sequence="200"><send>page %1=Away, back soon. Costs $5.</send></trigger>
          <trigger enabled="n" match="^You are (\w+)\.$" regexp="y" ignore_case="y" keep_evaluating="y" sequence="100"><send>say I am %1</send></trigger>
          <trigger enabled="y" match="*has arrived*" omit_from_output="y" sequence="300"></trigger>
          <trigger enabled="y" match="Your health is *" send_to="12" sequence="300"><send>UpdateGauge("%1")</send></trigger>
          <trigger enabled="y" match="* says *" script="OnSay" sequence="300"><send>nod</send></trigger>
          <trigger enabled="y" match="[unclosed" regexp="y" sequence="300"><send>x</send></trigger>
        </triggers>
        <aliases>
          <alias match="gg *" enabled="y"><send>get %1
        give %1 to Wren</send></alias>
          <alias match="sc" enabled="y" send_to="12"><send>DoScore()</send></alias>
        </aliases>
        <timers>
          <timer name="idle" enabled="y" minute="1" second="30.00" one_shot="y"><send>look</send></timer>
          <timer name="midnight" enabled="y" at_time="y"><send>say midnight</send></timer>
        </timers>
        <macros>
          <macro name="Ctrl+F2" type="send_now"><send>score</send></macro>
          <macro name="F3" type="replace"><send>say </send></macro>
          <macro name="up" type="send_now"><send>up</send></macro>
        </macros>
        <keypad>
          <key name="8"><send>north</send></key>
          <key name="Ctrl+2"><send>south</send></key>
          <key name="+"><send>up</send></key>
        </keypad>
        <script>Sub OnSay(a,b,c) : End Sub</script>
        <include name="health_bar.xml" plugin="y" />
        </muclient>
        """;

    private static ImportResult Read(string text, string source = "fixture")
    {
        var (result, problem) = ClientImport.Parse(text, source);
        if (result is null)
        {
            throw new InvalidOperationException(problem);
        }

        return result;
    }

    private static Trigger Named(ImportResult result, string name) => result.Set.Triggers.Single(t => t.Name == name);

    [Test]
    public async Task AMushclientWorldBringsItsWorldAndPlayer_AndHoldsThePasswordApart()
    {
        var result = Read(Mushclient, "Northwind.mcl");

        await Assert.That(result.Format).IsEqualTo(ImportFormat.MushClient);
        var world = result.Worlds.Single();
        await Assert.That(world.Name).IsEqualTo("Northwind");
        await Assert.That(world.Host).IsEqualTo("northwind.example.org");
        await Assert.That(world.Port).IsEqualTo(4201);
        await Assert.That(world.Characters.Single().Name).IsEqualTo("Wren");
        await Assert.That(world.Characters.Single().Password).IsNull().Because("a password is never put on a character by the reader");
        await Assert.That(result.Passwords.Single().Password).IsEqualTo("s3cret");
        await Assert.That(result.Passwords.Single().ToString()).DoesNotContain("s3cret");
    }

    [Test]
    public async Task AMushclientWildcardMatchesTheWholeLine_AndItsSendTakesTheCapture()
    {
        var result = Read(Mushclient);
        var page = Named(result, "* pages: *");

        await Assert.That(page.MatchType).IsEqualTo(TriggerMatchType.Wildcard).Because("starting and ending in * already means anywhere");
        await Assert.That(page.CaseSensitive).IsTrue();
        var match = page.Regex.Match("Ann pages: hello");
        await Assert.That(match.Success).IsTrue();
        await Assert.That(match.Result(page.Actions.SendResponse!)).IsEqualTo("page Ann=Away, back soon. Costs $5.");
        await Assert.That(page.Regex.IsMatch("Then Ann pages: hello")).IsTrue().Because("* at the start swallows the lead");
        await Assert.That(Named(result, "* pages: *").Regex.IsMatch("nobody paged")).IsFalse();
    }

    [Test]
    public async Task AMushclientPatternWithoutStarsAtBothEndsIsAnchored()
    {
        var result = Read("""<muclient><triggers><trigger enabled="y" match="You are hungry."><send>eat</send></trigger></triggers></muclient>""");
        var hungry = result.Set.Triggers.Single();

        await Assert.That(hungry.MatchType).IsEqualTo(TriggerMatchType.Regex);
        await Assert.That(hungry.Regex.IsMatch("You are hungry.")).IsTrue();
        await Assert.That(hungry.Regex.IsMatch("You are hungry. Very.")).IsFalse();
        await Assert.That(hungry.Regex.IsMatch("Then You are hungry.")).IsFalse();
    }

    [Test]
    public async Task AMushclientRegexKeepsItsFlags_AndRulesKeepTheirOrder()
    {
        var result = Read(Mushclient);
        var hungry = Named(result, "^You are (\\w+)\\.$");

        await Assert.That(hungry.Enabled).IsFalse();
        await Assert.That(hungry.CaseSensitive).IsFalse();
        await Assert.That(hungry.StopProcessing).IsFalse();
        await Assert.That(hungry.Regex.Match("you are HUNGRY.").Result(hungry.Actions.SendResponse!)).IsEqualTo("say I am HUNGRY");
        await Assert.That(result.Set.Triggers[0].Name).IsEqualTo(hungry.Name).Because("sequence 100 comes before 200");
    }

    [Test]
    public async Task AStarredMushclientPatternIsThisClientsWildcard_AndAnOmitIsAGag()
    {
        var result = Read(Mushclient);
        var arrived = Named(result, "*has arrived*");

        await Assert.That(arrived.MatchType).IsEqualTo(TriggerMatchType.Wildcard);
        await Assert.That(arrived.Actions.Gag).IsTrue();
        await Assert.That(arrived.Actions.SendResponse).IsNull();
        await Assert.That(arrived.Regex.IsMatch("Ann has arrived from the north.")).IsTrue();
    }

    [Test]
    public async Task ScriptsAndPluginsAreListedAndNeverImported()
    {
        var result = Read(Mushclient);

        await Assert.That(result.Set.Triggers.Any(t => t.Name.StartsWith("Your health", StringComparison.Ordinal))).IsFalse();
        await Assert.That(result.Set.Triggers.Any(t => t.Actions.ScriptCallback is not null)).IsFalse();
        await Assert.That(result.Set.Aliases.Any(a => a.Name == "sc")).IsFalse();
        await Assert.That(Named(result, "* says *").Actions.SendResponse).IsEqualTo("nod").Because("the send is kept, the script function is not");
        var reasons = result.Skipped.Select(s => $"{s.Kind}:{s.Name}:{s.Reason}").ToArray();
        await Assert.That(reasons).Contains(r => r.StartsWith("trigger:Your health is *:it sends to a script", StringComparison.Ordinal));
        await Assert.That(reasons).Contains(r => r.StartsWith("part:* says *:its script function OnSay", StringComparison.Ordinal));
        await Assert.That(reasons).Contains(r => r.StartsWith("script:", StringComparison.Ordinal));
        await Assert.That(reasons).Contains(r => r.StartsWith("plugin:health_bar.xml", StringComparison.Ordinal));
        await Assert.That(reasons).Contains(r => r.StartsWith("trigger:[unclosed:its regular expression", StringComparison.Ordinal));
    }

    [Test]
    public async Task MushclientAliasesTimersAndKeysMapWhereTheyCan()
    {
        var result = Read(Mushclient);
        var alias = result.Set.Aliases.Single();

        await Assert.That(alias.Regex.Match("gg sword").Result(alias.Substitution)).IsEqualTo("get sword\ngive sword to Wren");
        var timer = result.Set.Timers.Single();
        await Assert.That(timer.Name).IsEqualTo("idle");
        await Assert.That(timer.IntervalSeconds).IsEqualTo(90);
        await Assert.That(timer.OneShot).IsTrue();
        await Assert.That(result.Set.Macros.Select(m => $"{m.Key}={m.Command}").ToArray())
            .IsEquivalentTo(new[] { "Ctrl+F2=score", "Num8=north", "Ctrl+Num2=south" });
        var skipped = result.Skipped.Select(s => s.Name).ToArray();
        await Assert.That(skipped).Contains("midnight");
        await Assert.That(skipped).Contains("F3");
        await Assert.That(skipped).Contains("up").Because("a named macro has no key here, and \"up\" is not the arrow");
        await Assert.That(skipped).Contains("keypad +");
    }

    [Test]
    public async Task AFileNamingAnOutsideEntityExpandsNothing()
    {
        var hostile = """
            <?xml version="1.0"?>
            <!DOCTYPE muclient [<!ENTITY leak SYSTEM "file:///etc/passwd">]>
            <muclient><world name="&leak;" site="a.example" port="23" /></muclient>
            """;
        var (result, _) = ClientImport.Parse(hostile, "x.mcl");

        await Assert.That(result?.Worlds.SelectMany(w => new[] { w.Name, w.Host }).Any(t => t.Contains("root:", StringComparison.Ordinal)) ?? false).IsFalse();
    }

    [Test]
    public async Task SomethingThatIsNoClientsSetupIsRefusedOutLoud()
    {
        var (result, problem) = ClientImport.Parse("just some notes\nnothing here", "notes.txt");

        await Assert.That(result).IsNull();
        await Assert.That(problem).Contains("MUSHclient, Mudlet, TinTin++");
        var (old, oldProblem) = MushclientImport.Parse("\u0001\u0002binary muclient", "old.mcl");
        await Assert.That(old).IsNull();
        await Assert.That(oldProblem).Contains("not XML");
    }

    [Test]
    public async Task ACommandTooLongIsLeftOut_NeverCutShort()
    {
        var text = $"""<muclient><aliases><alias match="x" enabled="y"><send>{new string('a', ImportLimits.MaxText + 1)}</send></alias></aliases></muclient>""";
        var result = Read(text);

        await Assert.That(result.Set.Aliases).IsEmpty();
        await Assert.That(result.Skipped.Single().Reason).Contains("too long");
    }

    [Test]
    public async Task PastTheItemLimitRulesAreCountedNotImported()
    {
        var rules = string.Concat(Enumerable.Range(0, ImportLimits.MaxItems + 3).Select(i => $"""<alias match="a{i}" enabled="y"><send>x</send></alias>"""));
        var result = Read($"<muclient><aliases>{rules}</aliases></muclient>");

        await Assert.That(result.Set.Aliases.Count).IsEqualTo(ImportLimits.MaxItems);
        await Assert.That(result.SkippedCount).IsEqualTo(3);
    }

    [Test]
    public async Task ALargeFileIsRefusedBeforeItIsRead()
    {
        var path = Path.Combine(Path.GetTempPath(), "sharpmuterm-import-" + Guid.NewGuid().ToString("N") + ".mcl");
        try
        {
            using (var stream = File.Create(path))
            {
                stream.SetLength(ImportLimits.MaxFileBytes + 1);
            }

            var (result, problem) = ClientImport.Read(path);
            await Assert.That(result).IsNull();
            await Assert.That(problem).Contains("too large");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Mudlet ─────────────────────────────────────────────────────────────────────────────────────

    private const string Mudlet = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE MudletPackage>
        <MudletPackage version="1.001">
          <TriggerPackage>
            <TriggerGroup isActive="no" isFolder="yes">
              <name>Off folder</name>
              <script></script>
              <regexCodeList />
              <regexCodePropertyList />
              <Trigger isActive="yes" isFolder="no">
                <name>inside off</name>
                <script></script>
                <mCommand>wave</mCommand>
                <regexCodeList><string>waves to you</string></regexCodeList>
                <regexCodePropertyList><integer>0</integer></regexCodePropertyList>
              </Trigger>
            </TriggerGroup>
            <Trigger isActive="yes" isFolder="no">
              <name>kinds</name>
              <script></script>
              <mCommand>bow</mCommand>
              <regexCodeList><string>bows</string><string>Greetings</string><string>Hi.</string><string>^(\w+) curtsies$</string></regexCodeList>
              <regexCodePropertyList><integer>0</integer><integer>2</integer><integer>3</integer><integer>1</integer></regexCodePropertyList>
            </Trigger>
            <Trigger isActive="yes" isFolder="no">
              <name>lua only</name>
              <script>send("kill " .. matches[2])</script>
              <mCommand></mCommand>
              <regexCodeList><string>^(\w+) attacks you$</string></regexCodeList>
              <regexCodePropertyList><integer>1</integer></regexCodePropertyList>
            </Trigger>
            <Trigger isActive="yes" isFolder="no">
              <name>both</name>
              <script>echo("hi")</script>
              <mCommand>smile $1</mCommand>
              <regexCodeList><string>smiles</string></regexCodeList>
              <regexCodePropertyList><integer>0</integer></regexCodePropertyList>
            </Trigger>
            <Trigger isActive="yes" isFolder="no">
              <name>chain</name>
              <script></script>
              <mCommand>start</mCommand>
              <regexCodeList><string>begin</string></regexCodeList>
              <regexCodePropertyList><integer>0</integer></regexCodePropertyList>
              <Trigger isActive="yes" isFolder="no"><name>link</name><mCommand>x</mCommand><regexCodeList><string>y</string></regexCodeList><regexCodePropertyList><integer>0</integer></regexCodePropertyList></Trigger>
            </Trigger>
          </TriggerPackage>
          <AliasPackage>
            <Alias isActive="yes" isFolder="no"><name>k</name><script></script><command>kill rat</command><regex>^k$</regex></Alias>
          </AliasPackage>
          <TimerPackage>
            <Timer isActive="yes" isFolder="no" isTempTimer="no" isOffsetTimer="no"><name>tick</name><script></script><command>look</command><time>00:01:00.500</time></Timer>
          </TimerPackage>
          <KeyPackage>
            <Key isActive="yes" isFolder="no"><name>score</name><script></script><command>score</command><keyCode>16777265</keyCode><keyModifier>67108864</keyModifier></Key>
            <Key isActive="yes" isFolder="no"><name>pad</name><script></script><command>north</command><keyCode>56</keyCode><keyModifier>536870912</keyModifier></Key>
            <Key isActive="yes" isFolder="no"><name>bare</name><script></script><command>x</command><keyCode>65</keyCode><keyModifier>0</keyModifier></Key>
          </KeyPackage>
          <ScriptPackage><Script isActive="yes" isFolder="no"><name>helpers</name><script>function f() end</script></Script></ScriptPackage>
        </MudletPackage>
        """;

    [Test]
    public async Task AMudletPackageMapsItsPatternKinds_OneTriggerPerPattern()
    {
        var result = Read(Mudlet);

        await Assert.That(result.Format).IsEqualTo(ImportFormat.Mudlet);
        await Assert.That(Named(result, "kinds (1)").MatchType).IsEqualTo(TriggerMatchType.Contains);
        await Assert.That(Named(result, "kinds (2)").MatchType).IsEqualTo(TriggerMatchType.StartsWith);
        var exact = Named(result, "kinds (3)");
        await Assert.That(exact.Regex.IsMatch("Hi.")).IsTrue();
        await Assert.That(exact.Regex.IsMatch("Hi!")).IsFalse();
        await Assert.That(exact.Regex.IsMatch("Hi. there")).IsFalse();
        await Assert.That(Named(result, "kinds (4)").MatchType).IsEqualTo(TriggerMatchType.Regex);
    }

    [Test]
    public async Task AMudletRuleInADisabledFolderComesInDisabled()
    {
        var result = Read(Mudlet);

        await Assert.That(Named(result, "inside off").Enabled).IsFalse();
        await Assert.That(Named(result, "kinds (1)").Enabled).IsTrue();
    }

    [Test]
    public async Task MudletLuaIsNeverImported_AndACommandBesideItIsKept()
    {
        var result = Read(Mudlet);

        await Assert.That(result.Set.Triggers.Any(t => t.Name == "lua only")).IsFalse();
        var both = Named(result, "both");
        await Assert.That(both.Regex.Match("Ann smiles").Result(both.Actions.SendResponse!)).IsEqualTo("smile $1").Because("Mudlet sends its command as written");
        var reasons = result.Skipped.Select(s => $"{s.Kind}:{s.Name}:{s.Reason}").ToArray();
        await Assert.That(reasons).Contains(r => r.StartsWith("trigger:lua only:its action is Lua", StringComparison.Ordinal));
        await Assert.That(reasons).Contains(r => r.StartsWith("part:both:its Lua was left out", StringComparison.Ordinal));
        await Assert.That(reasons).Contains(r => r.StartsWith("script:helpers", StringComparison.Ordinal));
        await Assert.That(reasons).Contains(r => r.StartsWith("trigger:chain:it is a chain gating 1 rule", StringComparison.Ordinal));
        await Assert.That(result.Set.Triggers.Any(t => t.Name is "chain" or "link")).IsFalse();
    }

    [Test]
    public async Task MudletAliasesTimersAndKeysMap()
    {
        var result = Read(Mudlet);

        await Assert.That(result.Set.Aliases.Single().Regex.IsMatch("k")).IsTrue();
        await Assert.That(result.Set.Timers.Single().IntervalSeconds).IsEqualTo(60.5);
        await Assert.That(result.Set.Macros.Select(m => m.Key).ToArray()).IsEquivalentTo(new[] { "Ctrl+F2", "Num8" });
        await Assert.That(result.Skipped.Select(s => s.Name)).Contains("bare").Because("a bare letter is typing, not a key");
    }

    [Test]
    public async Task AMudletProfileFolderBringsItsWorldFromTheFilesBesideTheSaves()
    {
        var folder = Directory.CreateTempSubdirectory("sharpmuterm-mudlet-");
        try
        {
            var profile = Directory.CreateDirectory(Path.Combine(folder.FullName, "Achaea"));
            File.WriteAllText(Path.Combine(profile.FullName, "url"), "achaea.example.com\n");
            File.WriteAllText(Path.Combine(profile.FullName, "port"), "23");
            File.WriteAllText(Path.Combine(profile.FullName, "login"), "Ixia");
            File.WriteAllText(Path.Combine(profile.FullName, "password"), "pw");
            var current = Directory.CreateDirectory(Path.Combine(profile.FullName, "current"));
            File.WriteAllText(Path.Combine(current.FullName, "01-01-2024#10-00-00.xml"), Mudlet);

            var (result, problem) = ClientImport.Read(profile.FullName);

            await Assert.That(problem).IsNull();
            var world = result!.Worlds.Single();
            await Assert.That(world.Name).IsEqualTo("Achaea");
            await Assert.That(world.Host).IsEqualTo("achaea.example.com");
            await Assert.That(world.Port).IsEqualTo(23);
            await Assert.That(world.Characters.Single().Name).IsEqualTo("Ixia");
            await Assert.That(result.Passwords.Single().Password).IsEqualTo("pw");
            await Assert.That(result.Set.Triggers).IsNotEmpty();

            var (fromSave, _) = ClientImport.Read(Path.Combine(current.FullName, "01-01-2024#10-00-00.xml"));
            await Assert.That(fromSave!.Worlds.Single().Host).IsEqualTo("achaea.example.com").Because("a save inside current/ finds its profile");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Test]
    public async Task AMudletPackageZipIsReadWithoutExtractingAnything()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var evil = zip.CreateEntry("../../evil.lua").Open())
            {
                evil.Write(Encoding.UTF8.GetBytes("os.exit()"));
            }

            using var writer = new StreamWriter(zip.CreateEntry("Pack/Pack.xml").Open());
            writer.Write(Mudlet);
        }

        var (result, problem) = MudletImport.ReadZip(buffer.ToArray(), "Pack.mpackage");

        await Assert.That(problem).IsNull();
        await Assert.That(result!.Set.Aliases.Single().Name).IsEqualTo("k");
    }

    // ── TinTin++ ───────────────────────────────────────────────────────────────────────────────────

    private const string TinTin = """
        #nop A TinTin++ setup, written for this test.
        #session {nw} {northwind.example.org} {4201}
        #action {%1 tells you '%2'} {reply %2 to %1;smile}
        #act {^You are hungry.$} {eat bread}
        #action {%1 bonks you} {#showme ouch}
        #alias {k} {kill %1;loot %0}
        #alias {gt %1 %2} {give %2 to %1}
        #ticker {idle} {look} {120}
        #gag {^Ann has arrived}
        #highlight {red} {danger}
        #var hp 100
        """;

    [Test]
    public async Task ATinTinScriptBringsItsSessionActionsAliasesTickersAndGags()
    {
        var result = Read(TinTin, "setup.tin");

        await Assert.That(result.Format).IsEqualTo(ImportFormat.TinTin);
        var world = result.Worlds.Single();
        await Assert.That((world.Name, world.Host, world.Port)).IsEqualTo(("nw", "northwind.example.org", 4201));

        var tells = result.Set.Triggers.Single(t => t.Name.StartsWith("%1 tells", StringComparison.Ordinal));
        await Assert.That(tells.Regex.Match("Ann tells you 'hello there'").Result(tells.Actions.SendResponse!)).IsEqualTo("reply hello there to Ann\nsmile");

        var hungry = result.Set.Triggers.Single(t => t.Name.StartsWith("^You are", StringComparison.Ordinal));
        await Assert.That(hungry.Regex.IsMatch("You are hungry.")).IsTrue();
        await Assert.That(hungry.Regex.IsMatch("Then You are hungry.")).IsFalse();
        await Assert.That(hungry.Regex.IsMatch("You are hungryX")).IsFalse();

        var gag = result.Set.Triggers.Single(t => t.Actions.Gag);
        await Assert.That(gag.Regex.IsMatch("Ann has arrived from the west.")).IsTrue();

        var k = result.Set.Aliases.Single(a => a.Name == "k");
        await Assert.That(k.Regex.Match("k rat now").Result(k.Substitution)).IsEqualTo("kill rat\nloot rat now");
        var give = result.Set.Aliases.Single(a => a.Name.StartsWith("gt", StringComparison.Ordinal));
        await Assert.That(give.Regex.Match("gt Ann sword").Result(give.Substitution)).IsEqualTo("give sword to Ann");

        await Assert.That(result.Set.Timers.Single().IntervalSeconds).IsEqualTo(120);
    }

    [Test]
    public async Task TinTinsOwnStatementsAreLeftOut_AndCounted()
    {
        var result = Read(TinTin);

        await Assert.That(result.Set.Triggers.Any(t => t.Name.Contains("bonks", StringComparison.Ordinal))).IsFalse();
        await Assert.That(result.Skipped.Select(s => s.Reason)).Contains(r => r.Contains("runs TinTin commands", StringComparison.Ordinal));
        var other = result.Skipped.Single(s => s.Kind == "command");
        await Assert.That(other.Name).IsEqualTo("3 other TinTin commands");
        await Assert.That(other.Reason).Contains("#highlight");
    }

    // ── The plan ───────────────────────────────────────────────────────────────────────────────────

    private static AppConfiguration Existing() => new()
    {
        Worlds =
        {
            new WorldDefinition { Name = "Northwind", Host = "other.example.org", Port = 23 },
            new WorldDefinition
            {
                Name = "Home",
                Host = "NORTHWIND.example.org",
                Port = 4201,
                Characters = { new CharacterDefinition { Name = "wren", ConnectString = "mine" } },
            },
        },
        TriggerSets = { new TriggerSet { Name = "Imported from MUSHclient" } },
    };

    [Test]
    public async Task APlanOverwritesNothing_ASetNameClashGetsASuffix_AndAKnownEndpointOnlyGainsCharacters()
    {
        var config = Existing();
        var text = Mushclient.Replace("player=\"Wren\"", "player=\"Wren\"", StringComparison.Ordinal);
        var result = Read(text);
        result.Worlds[0].Characters.Add(new CharacterDefinition { Name = "Ash" });
        var plan = ImportPlan.For(config, result);

        await Assert.That(plan.SetName).IsEqualTo("Imported from MUSHclient (2)");
        var world = plan.Worlds.Single();
        await Assert.That(world.Existing).IsTrue();
        await Assert.That(world.Name).IsEqualTo("Home");
        await Assert.That(world.NewCharacters.ToArray()).IsEquivalentTo(new[] { "Ash" });
        await Assert.That(world.KeptCharacters.ToArray()).IsEquivalentTo(new[] { "Wren" });
        await Assert.That(plan.PasswordCount).IsEqualTo(0).Because("Wren's password is for a character this import does not add");

        var applied = plan.Apply(config, storePasswords: true);

        await Assert.That(config.TriggerSets.Count).IsEqualTo(2);
        await Assert.That(config.TriggerSets[0].Triggers).IsEmpty().Because("the existing set is untouched");
        var home = config.Worlds.Single(w => w.Name == "Home");
        await Assert.That(home.Characters.Single(c => c.Name == "wren").ConnectString).IsEqualTo("mine");
        await Assert.That(home.Characters.Single(c => c.Name == "Ash").TriggerSets.Single()).IsEqualTo("Imported from MUSHclient (2)");
        await Assert.That(home.Characters.Single(c => c.Name == "wren").TriggerSets).IsEmpty();
        await Assert.That(applied.Worlds).IsEqualTo(0);
        await Assert.That(applied.Characters).IsEqualTo(1);
    }

    [Test]
    public async Task ANewWorldWhoseNameIsTakenGetsASuffix_AndItsPasswordIsStoredOnlyWhenAsked()
    {
        var config = new AppConfiguration { Worlds = { new WorldDefinition { Name = "Northwind", Host = "x.example", Port = 1 } } };
        var result = Read(Mushclient);

        var plan = ImportPlan.For(config, result);
        await Assert.That(plan.Worlds.Single().Name).IsEqualTo("Northwind (2)");
        await Assert.That(plan.PasswordCount).IsEqualTo(1);
        plan.Apply(config, storePasswords: false);
        await Assert.That(config.Worlds[1].Characters.Single().Password).IsNull();

        var again = new AppConfiguration();
        ImportPlan.For(again, result).Apply(again, storePasswords: true);
        await Assert.That(again.Worlds.Single().Characters.Single().Password).IsEqualTo("s3cret");
        await Assert.That(again.TriggerSets.Single().Triggers.Count).IsEqualTo(result.Set.Triggers.Count);
        await Assert.That(ReferenceEquals(again.TriggerSets.Single().Triggers[0], result.Set.Triggers[0])).IsFalse().Because("the configuration gets copies");
    }

    [Test]
    public async Task APlanMadeBeforeTheConfigurationChangedRefusesToApply()
    {
        var config = new AppConfiguration();
        var plan = ImportPlan.For(config, Read(Mushclient));
        config.Worlds.Add(new WorldDefinition { Name = "Northwind", Host = "y.example", Port = 2 });

        await Assert.That(() => plan.Apply(config, storePasswords: false)).Throws<InvalidOperationException>();
        await Assert.That(config.TriggerSets).IsEmpty();
    }
}
