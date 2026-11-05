using System.Security.Cryptography;
using System.Text;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests;

/// <summary>
/// The spelling dictionary fetched when the player asks (<see cref="DictionaryDownload"/>): https only, capped, checked
/// against a pinned SHA-256 before anything is written, and found by <see cref="SpellChecker.Find"/> once it is.
/// </summary>
public class DictionaryDownloadTests
{
    private static readonly byte[] Aff = Encoding.UTF8.GetBytes("SET UTF-8\n");
    private static readonly byte[] Dic = Encoding.UTF8.GetBytes("3\nhello\nworld\npose\n");

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static DictionarySource Source(string scheme = "https", string? dicSha = null) => new(
        "en_TEST", "English (test)", "a test", "public domain",
        new Uri($"{scheme}://example.org/en_TEST.aff"), Sha(Aff),
        new Uri($"{scheme}://example.org/en_TEST.dic"), dicSha ?? Sha(Dic),
        Aff.Length + Dic.Length);

    private static Task<byte[]?> Serve(Uri uri, long cap, CancellationToken token) =>
        Task.FromResult<byte[]?>(uri.AbsolutePath.EndsWith(".aff", StringComparison.Ordinal) ? Aff : Dic);

    private static string Folder() => Path.Combine(Path.GetTempPath(), "smt-dict-" + Guid.NewGuid().ToString("N"));

    [Test]
    public async Task AFetchedPairIsWrittenAndFoundBySpelling()
    {
        var folder = Folder();
        try
        {
            await Assert.That(await DictionaryDownload.InstallAsync(folder, Source(), Serve)).IsNull();
            await Assert.That(DictionaryDownload.Holds(folder)).IsTrue();
            await Assert.That(Directory.GetFiles(folder, "*.new")).IsEmpty();
            var spelling = SpellChecker.Find([folder], "en_TEST", null);
            await Assert.That(spelling).IsNotNull();
            await Assert.That(spelling!.Check("hello")).IsTrue();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Test]
    public async Task BytesThatAreNotThePinnedFileWriteNothing()
    {
        var folder = Folder();
        var problem = await DictionaryDownload.InstallAsync(folder, Source(dicSha: new string('0', 64)), Serve);
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!).Contains("SHA-256");
        await Assert.That(Directory.Exists(folder)).IsFalse();
    }

    [Test]
    public async Task OnlyHttpsIsFetched()
    {
        var asked = 0;
        var problem = await DictionaryDownload.InstallAsync(Folder(), Source("http"), (u, c, t) =>
        {
            asked++;
            return Serve(u, c, t);
        });
        await Assert.That(problem).IsNotNull();
        await Assert.That(asked).IsEqualTo(0);
    }

    [Test]
    public async Task ADownloadOverTheCapIsRefused()
    {
        var folder = Folder();
        var problem = await DictionaryDownload.InstallAsync(folder, Source(), (_, cap, _) => Task.FromResult<byte[]?>(new byte[cap + 1]));
        await Assert.That(problem).IsNotNull();
        await Assert.That(Directory.Exists(folder)).IsFalse();
        await Assert.That(DictionaryDownload.Holds(null)).IsFalse();
    }

    [Test]
    public async Task ThePinnedSourcesAreHttpsAndTheLanguageChoosesOne()
    {
        foreach (var source in DictionaryDownload.Catalog)
        {
            await Assert.That(source.Aff.Scheme).IsEqualTo("https");
            await Assert.That(source.Dic.Scheme).IsEqualTo("https");
            await Assert.That(source.AffSha256.Length).IsEqualTo(64);
            await Assert.That(source.DicSha256.Length).IsEqualTo(64);
        }

        await Assert.That(DictionaryDownload.For("en-GB")).IsEqualTo(DictionaryDownload.EnglishGb);
        await Assert.That(DictionaryDownload.For("en_AU.UTF-8")).IsEqualTo(DictionaryDownload.EnglishGb);
        await Assert.That(DictionaryDownload.For("fr_FR")).IsEqualTo(DictionaryDownload.EnglishUs);
        await Assert.That(DictionaryDownload.For(null)).IsEqualTo(DictionaryDownload.EnglishUs);
    }
}
