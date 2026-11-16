using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Text;

/// <summary>
/// A world's or a character's name made into a piece of a file name every file system takes. The case that was
/// reported is the first: a MUSH character with a space in the name is connected as <c>"first last"</c>, and the
/// quotes made the session log impossible to create on Windows.
/// </summary>
public class PortableFileNameTests
{
    [Test]
    public async Task AQuotedNameLosesItsQuotes() =>
        await Assert.That(PortableFileName.Of("\"first last\"")).IsEqualTo("first last");

    [Test]
    [Arguments("Corvid")]
    [Arguments("Aetherfall")]
    [Arguments("Ann Marie")]
    [Arguments("O'Brien")]
    [Arguments("a.b-c_d")]
    [Arguments("Zoë")]
    public async Task ANameThatNeedsNothingIsUnchanged(string name) =>
        await Assert.That(PortableFileName.Of(name)).IsEqualTo(name);

    [Test]
    public async Task EveryCharacterOneOfTheSystemsRefusesIsGone()
    {
        var name = PortableFileName.Of("a<b>c:d/e\\f|g?h*i\"j\u0001k\u007fl\tm");
        await Assert.That(name).IsEqualTo("a_b_c_d_e_f_g_h_ijklm");
        await Assert.That(name.IndexOfAny(['<', '>', ':', '/', '\\', '|', '?', '*', '"'])).IsEqualTo(-1);
        await Assert.That(name.Any(char.IsControl)).IsFalse();
    }

    [Test]
    public async Task ASeparatorIsReplacedSoTwoNamesDoNotBecomeOne() =>
        await Assert.That(PortableFileName.Of("Chat/OOC")).IsNotEqualTo(PortableFileName.Of("ChatOOC"));

    [Test]
    [Arguments("name.", "name")]
    [Arguments("name . . ", "name")]
    [Arguments("  name", "name")]
    [Arguments("\"name \"", "name")]
    public async Task EdgeSpacesAndTrailingDotsGo(string name, string expected) =>
        await Assert.That(PortableFileName.Of(name)).IsEqualTo(expected);

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("\"\"")]
    [Arguments(" . ")]
    [Arguments("...")]
    [Arguments("\u0001\u0002")]
    public async Task NothingLeftIsTheFallback(string? name)
    {
        await Assert.That(PortableFileName.Of(name)).IsEqualTo("_");
        await Assert.That(PortableFileName.Of(name, "anonymous")).IsEqualTo("anonymous");
    }

    [Test]
    [Arguments("CON", "CON_")]
    [Arguments("nul", "nul_")]
    [Arguments("Com1", "Com1_")]
    [Arguments("LPT9", "LPT9_")]
    [Arguments("con.txt", "con_.txt")]
    [Arguments("\"aux\"", "aux_")]
    [Arguments("CON.Corvid", "CON_.Corvid")]
    [Arguments("CONSOLE", "CONSOLE")]
    [Arguments("COM10", "COM10")]
    [Arguments("Connie", "Connie")]
    public async Task AWindowsDeviceNameIsNotLeftAsOne(string name, string expected) =>
        await Assert.That(PortableFileName.Of(name)).IsEqualTo(expected);

    [Test]
    public async Task ALongNameIsCutWithoutLeavingAnEndThatWouldBeTrimmed()
    {
        var name = PortableFileName.Of(new string('a', 63) + ". tail");
        await Assert.That(name).IsEqualTo(new string('a', 63));
        await Assert.That(PortableFileName.Of(new string('b', 200)).Length).IsEqualTo(PortableFileName.MaxLength);
    }

    [Test]
    public async Task ACutNeverSplitsASurrogatePair()
    {
        var name = PortableFileName.Of(new string('a', 63) + "😀");
        await Assert.That(name).IsEqualTo(new string('a', 63));
    }

    [Test]
    public async Task TheResultCanBeCreatedOnThisSystem()
    {
        var folder = Path.Combine(Path.GetTempPath(), "smt-portable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var raw in new[] { "\"first last\"", "a<b>c:d", "CON", "x/../y", "..", " . " })
            {
                var path = Path.Combine(folder, PortableFileName.Of(raw) + ".log");
                await File.WriteAllTextAsync(path, raw);
                await Assert.That(Path.GetDirectoryName(Path.GetFullPath(path))).IsEqualTo(Path.GetFullPath(folder));
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
