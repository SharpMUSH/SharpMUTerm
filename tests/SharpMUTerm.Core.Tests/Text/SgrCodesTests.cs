using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Text;

public class SgrCodesTests
{
    [Test]
    public async Task Apply_EmptyParameters_ResetsToDefault()
    {
        var bold = new TextStyle(TerminalColor.FromIndex(1), TerminalColor.Default, TextAttributes.Bold);

        await Assert.That(SgrCodes.Apply(bold, string.Empty)).IsEqualTo(TextStyle.Default);
    }

    [Test]
    public async Task Apply_SetsForegroundFromAnIndexedCode()
    {
        var result = SgrCodes.Apply(TextStyle.Default, "33");

        await Assert.That(result.Foreground).IsEqualTo(TerminalColor.FromIndex(3));
    }

    [Test]
    public async Task Apply_KeepsUnrelatedStateWhenOnlyOneAttributeChanges()
    {
        var yellow = SgrCodes.Apply(TextStyle.Default, "33");

        var result = SgrCodes.Apply(yellow, "1");

        await Assert.That(result.Foreground).IsEqualTo(TerminalColor.FromIndex(3));
        await Assert.That(result.Attributes.HasFlag(TextAttributes.Bold)).IsTrue();
    }
}
