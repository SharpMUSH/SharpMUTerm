using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Core.Tests.Telnet;

/// <summary>
/// MCP 2.1 from the client's side, against a scripted MOO: the server's offer is answered, simpleedit hands
/// a buffer over and takes it back, vmoo-client hears who this is, and a line a player typed is nobody's.
/// </summary>
public class McpEditTests
{
    private const string Offer = "#$#mcp version: \"2.1\" to: \"2.1\"\r\n";

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private sealed class Moo : IAsyncDisposable
    {
        public ScriptedTransport Transport { get; } = new() { Greeting = Ascii("Welcome to the MOO.\r\n" + Offer) };

        public TelnetSession Session { get; }

        public List<string> Output { get; } = [];

        public List<MooEdit> Edits { get; } = [];

        public string Key { get; private set; } = string.Empty;

        public Moo()
        {
            Session = new TelnetSession(Transport, NullLogger.Instance);
            Session.OutputReceived += (_, e) => { lock (Output) Output.Add(e.Text); };
            Session.MooEditReceived += (_, e) => { lock (Edits) Edits.Add(e); };
        }

        public string Sent => Encoding.ASCII.GetString(Transport.Sent);

        /// <summary>Connects, reads the key the client chose, and agrees the packages a MOO with them would.</summary>
        public Task OpenAsync(params string[] packages) => OpenAsync(() => Task.CompletedTask, packages);

        public async Task OpenAsync(Func<Task> beforeAgreeing, params string[] packages)
        {
            await Session.ConnectAsync();
            await beforeAgreeing();
            await Until(() => Regex.IsMatch(Sent, "#\\$#mcp-negotiate-end \\S+\r\n"));
            Key = Regex.Match(Sent, "authentication-key: \"?([^\" ]+)\"?").Groups[1].Value;
            var reply = new StringBuilder();
            foreach (var package in new[] { "mcp-negotiate" }.Concat(packages))
            {
                var max = package == "mcp-negotiate" ? "2.0" : "1.0";
                reply.Append($"#$#mcp-negotiate-can {Key} package: \"{package}\" min-version: \"1.0\" max-version: \"{max}\"\r\n");
            }

            reply.Append($"#$#mcp-negotiate-end {Key}\r\n");
            Transport.SendToClient(Ascii(reply.ToString()));
        }

        public void Send(string text) => Transport.SendToClient(Ascii(text));

        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    [Test]
    public async Task TheOfferIsAnsweredWithTheEditingPackages()
    {
        await using var moo = new Moo();
        await moo.OpenAsync();

        await Assert.That(moo.Key).IsNotEmpty();
        await Assert.That(moo.Sent).Contains("package: \"dns-org-mud-moo-simpleedit\"");
        await Assert.That(moo.Sent).Contains("package: \"dns-com-vmoo-client\"");
        await Assert.That(moo.Sent).Contains("package: \"dns-com-awns-status\"");
        await Task.Delay(50);
        string[] seen;
        lock (moo.Output) seen = [.. moo.Output];

        // The offer is protocol, not text.
        await Assert.That(seen.Any(l => l.Contains("#$#", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task ASilentServerHearsNoMcp()
    {
        var transport = new ScriptedTransport { Greeting = Ascii("Welcome\r\n") };
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        await session.ConnectAsync();
        await Task.Delay(150);

        await Assert.That(Encoding.ASCII.GetString(transport.Sent)).DoesNotContain("#$#");
    }

    [Test]
    public async Task ContentArrivesAsAnEditAndTheSaveGoesBackUnderItsReference()
    {
        await using var moo = new Moo();
        await moo.OpenAsync("dns-org-mud-moo-simpleedit");
        moo.Send(
            $"#$#dns-org-mud-moo-simpleedit-content {moo.Key} reference: \"#98.look\" name: \"Test:look\" type: \"moo-code\" content*: \"\" _data-tag: \"77\"\r\n"
            + "#$#* 77 content: player:tell(\"Hi.\");\r\n"
            + "#$#* 77 content: return 1;\r\n"
            + "#$#: 77\r\n");
        await Until(() => { lock (moo.Edits) return moo.Edits.Count > 0; });

        var edit = moo.Edits.Single();
        await Assert.That(edit).IsEqualTo(edit with { Reference = "#98.look", Name = "Test:look", Type = "moo-code" });
        await Assert.That(edit.Lines).IsEquivalentTo(new[] { "player:tell(\"Hi.\");", "return 1;" });

        await moo.Session.SendMooEditAsync(edit, ["player:tell(\"Hello.\");", "return 1;"]);
        await Until(() => moo.Sent.Contains("#$#: ", StringComparison.Ordinal));

        var sent = moo.Sent;
        await Assert.That(sent).Contains($"#$#dns-org-mud-moo-simpleedit-set {moo.Key} reference: \"#98.look\" type: \"moo-code\" content*: \"\"");
        await Assert.That(sent).Contains("content: player:tell(\"Hello.\");\r\n");
        await Assert.That(sent).Contains("content: return 1;\r\n");
    }

    [Test]
    public async Task AnEditTypedByAPlayerIsNeitherOpenedNorShown()
    {
        await using var moo = new Moo();
        await moo.OpenAsync("dns-org-mud-moo-simpleedit");
        moo.Send("#$#dns-org-mud-moo-simpleedit-content 0000 reference: \"#1.x\" name: \"x\" type: \"moo-code\" content*: \"\" _data-tag: \"9\"\r\n"
            + "#$#* 9 content: @shutdown\r\n#$#: 9\r\nAfter.\r\n");
        await Until(() => { lock (moo.Output) return moo.Output.Contains("After."); });

        await Assert.That(moo.Edits).IsEmpty();
        string[] seen;
        lock (moo.Output) seen = [.. moo.Output];
        await Assert.That(seen.Any(l => l.Contains("shutdown", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task ThePeerHearsWhoThisIsAndHowWide()
    {
        await using var moo = new Moo();
        await moo.OpenAsync(async () => await moo.Session.SetWindowSizeAsync(100, 40), "dns-com-vmoo-client");
        await Until(() => moo.Sent.Contains("screensize", StringComparison.Ordinal));

        await Assert.That(moo.Sent).Contains($"#$#dns-com-vmoo-client-info {moo.Key} name: \"SharpMUTerm\"");
        await Assert.That(moo.Sent).Contains($"#$#dns-com-vmoo-client-screensize {moo.Key} cols: \"100\" rows: \"40\"");

        // A resize after agreement is told to the MOO as well as to NAWS.
        await moo.Session.SetWindowSizeAsync(90, 30);
        await Until(() => moo.Sent.Contains("cols: \"90\"", StringComparison.Ordinal));
        await Assert.That(moo.Sent).Contains($"#$#dns-com-vmoo-client-screensize {moo.Key} cols: \"90\" rows: \"30\"");
    }

    [Test]
    public async Task StatusTextIsRaised()
    {
        await using var moo = new Moo();
        var status = new List<string>();
        moo.Session.McpStatusReceived += (_, text) => { lock (status) status.Add(text); };
        await moo.OpenAsync("dns-com-awns-status");
        moo.Send($"#$#dns-com-awns-status {moo.Key} text: \"You are hungry.\"\r\n");
        await Until(() => { lock (status) return status.Count > 0; });

        await Assert.That(status.Single()).IsEqualTo("You are hungry.");
    }

    [Test]
    public async Task APromptAfterAConsumedLineIsOnlyThePrompt()
    {
        await using var moo = new Moo();
        await moo.OpenAsync("dns-com-awns-status");
        var prompts = new List<string>();
        moo.Session.OutputReceived += (_, e) => { if (e.IsPrompt) lock (prompts) prompts.Add(e.Text); };
        moo.Transport.SendToClient([.. Ascii($"#$#dns-com-awns-status {moo.Key} text: \"x\"\r\n> "), 255, 249]);
        await Until(() => { lock (prompts) return prompts.Count > 0; });

        await Assert.That(prompts.Single()).IsEqualTo("> ");
    }

    [Test]
    public async Task LinesOfKeepsInteriorBlanksAndDropsOnlyTheCaretsNewline()
    {
        await Assert.That(MooEditing.LinesOf("a\r\n\r\nb\n")).IsEquivalentTo(new[] { "a", "", "b" });
        await Assert.That(MooEditing.LinesOf("  indented\n")).IsEquivalentTo(new[] { "  indented" });
        await Assert.That(MooEditing.LinesOf(string.Empty)).IsEmpty();
    }
}
