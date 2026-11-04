using SharpMUTerm.Core.Links;

namespace SharpMUTerm.Core.Tests.Links;

/// <summary>
/// <c>telnet://</c> links are text anybody can write and the desktop hands this client on a click, so the reader is
/// held to what <c>/connect host port</c> can do: a host, a port, TLS for <c>telnets</c>, and nothing else.
/// </summary>
public class TelnetLinkTests
{
    [Test]
    [Arguments("telnet://mud.example.org:4201", "mud.example.org", 4201, false)]
    [Arguments("telnet://mud.example.org:4201/", "mud.example.org", 4201, false)]
    [Arguments("TELNET://Mud.Example.org:4201", "Mud.Example.org", 4201, false)]
    [Arguments("telnet://mud.example.org", "mud.example.org", 23, false)]
    [Arguments("telnets://mud.example.org", "mud.example.org", 992, true)]
    [Arguments("telnets://mud.example.org:6666", "mud.example.org", 6666, true)]
    [Arguments("telnet://192.0.2.7:4000", "192.0.2.7", 4000, false)]
    [Arguments("telnet://[2001:db8::1]:4000", "2001:db8::1", 4000, false)]
    [Arguments("telnet://[::1]", "::1", 23, false)]
    public async Task AWellFormedLinkIsAHostAPortAndWhetherItIsTls(string text, string host, int port, bool tls)
    {
        var link = TelnetLink.TryParse(text, out var refusal);

        await Assert.That(refusal).IsNull();
        await Assert.That(link).IsEqualTo(new TelnetLink(host, port, tls));
    }

    [Test]
    [Arguments("telnet://mud.example.org:0")]
    [Arguments("telnet://mud.example.org:65536")]
    [Arguments("telnet://mud.example.org:99999999999")]
    [Arguments("telnet://mud.example.org:-23")]
    [Arguments("telnet://mud.example.org:+23")]
    [Arguments("telnet://mud.example.org:23a")]
    [Arguments("telnet://mud.example.org:")]
    public async Task APortOutOfRangeOrNotPlainDigitsIsRefused(string text) =>
        await Assert.That(TelnetLink.TryParse(text)).IsNull();

    [Test]
    [Arguments("telnet://wizard:hunter2@mud.example.org:4201")]
    [Arguments("telnet://wizard@mud.example.org")]
    [Arguments("telnet://@mud.example.org")]
    [Arguments("telnet://mud.example.org:4201@evil.example")]
    public async Task EmbeddedCredentialsAreRefusedRatherThanSentOrDropped(string text)
    {
        var link = TelnetLink.TryParse(text, out var refusal);

        await Assert.That(link).IsNull();
        await Assert.That(refusal).Contains("user name or password");
    }

    [Test]
    [Arguments("telnet://mud.example.org:4201/say hello")]
    [Arguments("telnet://mud.example.org:4201/connect%20wizard%20pw")]
    [Arguments("telnet://mud.example.org:4201/?cmd=@shutdown")]
    [Arguments("telnet://mud.example.org:4201?cmd=quit")]
    [Arguments("telnet://mud.example.org:4201#frag")]
    [Arguments("telnet://mud.example.org:4201//")]
    [Arguments("telnet://mud.example.org/path")]
    public async Task APathQueryOrFragmentIsRefused(string text) =>
        await Assert.That(TelnetLink.TryParse(text)).IsNull();

    [Test]
    [Arguments("telnet://mud.example.org:4201\r\n@shutdown")]
    [Arguments("telnet://mud.example.org\r\n")]
    [Arguments("telnet://mud.example.org\n:4201")]
    [Arguments("telnet://mud.example.org:4201\u0000")]
    [Arguments("telnet://mud.example.org\u001b]2;owned\u0007")]
    [Arguments("telnet://mud example.org")]
    [Arguments(" telnet://mud.example.org")]
    [Arguments("telnet://mud.example.org\t")]
    [Arguments("telnet://mud%2Eexample.org")]
    [Arguments("telnet://mud.example.org%0d%0a")]
    [Arguments("telnet://mud\\example.org")]
    [Arguments("telnet://müd.example.org")]
    public async Task ControlCharactersSpacesEscapesAndNonAsciiAreRefused(string text) =>
        await Assert.That(TelnetLink.TryParse(text)).IsNull();

    [Test]
    [Arguments("")]
    [Arguments("telnet://")]
    [Arguments("telnet:///")]
    [Arguments("telnet:mud.example.org")]
    [Arguments("http://mud.example.org")]
    [Arguments("ssh://mud.example.org")]
    [Arguments("telnet://2001:db8::1")]
    [Arguments("telnet://[2001:db8::1")]
    [Arguments("telnet://[]:23")]
    [Arguments("telnet://mud..example")]
    [Arguments("telnet://a:b:c")]
    public async Task AnythingElseIsRefused(string text) =>
        await Assert.That(TelnetLink.TryParse(text)).IsNull();

    [Test]
    public async Task AnOverlongLinkIsRefusedBeforeItIsRead()
    {
        var text = "telnet://" + new string('a', TelnetLink.MaxLength) + ".example";

        var link = TelnetLink.TryParse(text, out var refusal);

        await Assert.That(link).IsNull();
        await Assert.That(refusal).Contains("too long");
    }

    [Test]
    public async Task AnIpv6HostIsBracketedAgainForTheCommand()
    {
        var link = TelnetLink.TryParse("telnets://[2001:db8::1]:992")!;

        await Assert.That(link.HostForCommand).IsEqualTo("[2001:db8::1]");
        await Assert.That(link.ToString()).IsEqualTo("telnets://[2001:db8::1]:992");
    }

    [Test]
    [Arguments("telnet://x", true)]
    [Arguments("TelNetS://x", true)]
    [Arguments("mud.example.org", false)]
    [Arguments("--tls", false)]
    public async Task OnlyTheTwoSchemesLookLikeALink(string text, bool expected) =>
        await Assert.That(TelnetLink.LooksLikeLink(text)).IsEqualTo(expected);
}
