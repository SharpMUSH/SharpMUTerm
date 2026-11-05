using SharpMUTerm.Client.Chrome;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// The notice a world added says (wave 32): F5 once — it used to carry an F5 chip beside a sentence ending "F5 edits it",
/// so the status row and the toast both said F5 twice — and the encoding the world was saved with, when one was.
/// </summary>
public class WorldNoticeTests
{
    [Test]
    public async Task AnAddedWorldNamesF5Once_AndTheEncodingItWasSavedWith()
    {
        var text = WorldNotices.Added("Aetherfall", "aetherfall.example", 4201, "ISO-8859-1");

        await Assert.That(text).IsEqualTo("added Aetherfall (aetherfall.example:4201), saved with encoding ISO-8859-1 — F5 edits it");
        await Assert.That(text.Split("F5").Length - 1).IsEqualTo(1);
    }

    [Test]
    [Arguments("auto")]
    [Arguments("AUTO")]
    [Arguments("")]
    [Arguments(null)]
    public async Task AWorldLeftToNegotiateItsEncodingSaysNoEncoding(string? encoding)
    {
        await Assert.That(WorldNotices.Added("mud.example.org", "mud.example.org", 4000, encoding))
            .IsEqualTo("added mud.example.org (mud.example.org:4000) — F5 edits it");
    }
}
