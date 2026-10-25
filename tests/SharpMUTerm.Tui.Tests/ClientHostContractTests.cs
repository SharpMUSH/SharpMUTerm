using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The rules that keep the terminal and the desktop window one client. The compiler holds most of it — a
/// <c>required</c> member on <see cref="ClientHost"/> must be set by every host, and the banned-API analyzer
/// refuses a host check outside <c>Hosting/</c> — and these pin the parts it cannot see.
/// </summary>
public class ClientHostContractTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    /// <summary>
    /// Every member of the host contract is <c>required</c>. One with a default is a member a new host can
    /// forget, and forgetting is how a host gets the terminal's answer to a question nobody asked it.
    /// </summary>
    [Test]
    [Arguments(typeof(ClientHost))]
    [Arguments(typeof(HostKeys))]
    public async Task EveryMemberOfTheHostContractIsRequired(Type contract)
    {
        var loose = contract
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.SetMethod is not null && p.GetCustomAttribute<RequiredMemberAttribute>() is null)
            .Select(p => p.Name)
            .ToList();

        await Assert.That(loose).IsEmpty().Because($"{contract.Name} members every host must state");
    }

    /// <summary>A host's newline chord is one it delivers to the client, not one it keeps for itself.</summary>
    [Test]
    public async Task TheTerminalKeepsNoChordAndItsNewlineIsAltEnter()
    {
        await Assert.That(HostKeys.Terminal.Reserved).IsEmpty();
        await Assert.That(HostKeys.Terminal.Newline.Glyph).IsEqualTo("Alt+⏎");
    }

    /// <summary>The headless host is the snapshot's: no loop, no clipboard, no browser, no focus reports.</summary>
    [Test]
    public async Task TheHeadlessHostTouchesNothingOutsideTheProcess()
    {
        using var driver = new HeadlessConsoleDriver(80, 24);
        var host = ClientHost.Headless(driver, Caps);

        await Assert.That(host.RunsInline).IsTrue();
        await Assert.That(host.Clipboard).IsNull();
        await Assert.That(host.OpenUrl).IsNull();
        await Assert.That(host.FocusReporting).IsFalse();
    }

    /// <summary>The app keeps the host it was given, and answers the ⌃P newline entry from its keys.</summary>
    [Test]
    public async Task TheCatalogNamesTheHostsNewlineChord()
    {
        Console.SetIn(TextReader.Null);
        using var driver = new HeadlessConsoleDriver(160, 48);
        var keys = new HostKeys
        {
            Newline = new HostChord(ConsoleKey.Enter, ConsoleModifiers.Control, "Ctrl+⏎", "Ctrl+Enter"),
            Reserved = [],
        };
        await using var app = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(driver, Caps) with { Keys = keys });

        var entry = app.BuildCatalog().Single(c => c.Id == "term:newline");
        await Assert.That(entry.Subtitle).IsEqualTo("Ctrl+⏎");
    }

    /// <summary>Help reads the host's keys too: the same page, with each host's newline in it.</summary>
    [Test]
    public async Task HelpNamesTheHostsNewlineChord()
    {
        var keys = HostKeys.Terminal with
        {
            Newline = new HostChord(ConsoleKey.Enter, ConsoleModifiers.Control, "Ctrl+⏎", "Ctrl+Enter"),
        };
        var help = new StringWriter();
        ClientUsage.Write(help, ClientUsage.Terminal, keys);

        await Assert.That(help.ToString()).Contains("Ctrl+Enter inserts a newline");
        await Assert.That(help.ToString()).DoesNotContain("Alt+Enter inserts a newline");
    }

    /// <summary>An option's value is never read as the host: <c>--name Corvid host</c> dials host.</summary>
    [Test]
    public async Task AnOptionsValueIsNotThePositionalHost()
    {
        await Assert.That(ClientOptions.Positional(["--name", "Corvid", "example.org", "4201", "--tls"]))
            .IsEquivalentTo(new[] { "example.org", "4201" });
    }

    /// <summary>
    /// Every <c>--view</c> <see cref="SharpMUTermApp.RenderSnapshot"/> recognises is in
    /// <see cref="SnapshotViews.All"/>, which the window parity test renders through both drivers. Read off
    /// the source, because the views are a chain of comparisons rather than a table.
    /// </summary>
    [Test]
    public async Task EveryViewTheSnapshotKnowsIsInTheParityList()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SharpMUTerm.Tui", "SharpMUTermApp.cs"));
        var exact = Regex.Matches(source, @"string\.Equals\(view, ""([a-z0-9-]+)""").Select(m => m.Groups[1].Value);
        var prefixes = Regex.Matches(source, @"view\.StartsWith\(""([a-z0-9-]+)""").Select(m => m.Groups[1].Value);
        var screens = Regex.Matches(source, @"new\(ConsoleKey\.F\d+, ""[^""]+"", new\[\] \{ ([^}]+) \}")
            .SelectMany(m => Regex.Matches(m.Groups[1].Value, @"""([a-z0-9-]+)""").Select(v => v.Groups[1].Value));

        var listed = SnapshotViews.All.OfType<string>().ToHashSet(StringComparer.Ordinal);
        var missing = exact.Concat(screens).Where(v => !listed.Contains(v))
            .Concat(prefixes.Where(p => !listed.Any(v => v.StartsWith(p, StringComparison.Ordinal))))
            .Distinct()
            .ToList();

        await Assert.That(exact.Count()).IsGreaterThan(20).Because("the pattern still finds the view comparisons");
        await Assert.That(missing).IsEmpty().Because("each view has to be rendered in the window too");
    }

    internal static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SharpMUTerm.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("SharpMUTerm.slnx not found above " + AppContext.BaseDirectory);
    }
}
