using System.Security.Cryptography;
using SharpMUTerm.Core.Packages;

namespace SharpMUTerm.Core.Tests.Packages;

public class PackageIndexTests
{
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Entry(string id, string games, string url = "https://p.example/x.zip", string? sha = null, string version = "1.0.0") =>
        $$"""{"id":"{{id}}","name":"{{id}}","version":"{{version}}","games":[{{games}}],"url":"{{url}}","sha256":"{{sha ?? new string('a', 64)}}"}""";

    [Test]
    public async Task AnIndexReadsItsEntries()
    {
        var (index, problem) = PackageIndex.Parse($$"""{"packages":[{{Entry("a.one", "\"aetherfall.mux:4201\"")}}]}""");

        await Assert.That(problem).IsNull();
        await Assert.That(index!.Entries.Single().Id).IsEqualTo("a.one");
        await Assert.That(index.Dropped).IsEqualTo(0);
    }

    [Test]
    [Arguments("http://p.example/x.zip", null)]
    [Arguments("file:///etc/passwd", null)]
    [Arguments("https://p.example/x.zip", "abc")]
    [Arguments("https://p.example/x.zip", "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public async Task AnEntryThatBreaksARuleIsDroppedAndCounted(string url, string? sha)
    {
        var (index, _) = PackageIndex.Parse($$"""{"packages":[{{Entry("a.one", "\"*\"", url, sha)}},{{Entry("b.two", "\"*\"")}}]}""");

        await Assert.That(index!.Entries.Select(e => e.Id)).IsEquivalentTo(new[] { "b.two" });
        await Assert.That(index.Dropped).IsEqualTo(1);
    }

    [Test]
    public async Task ABadManifestOrARepeatedIdIsDropped()
    {
        var (index, _) = PackageIndex.Parse($$"""{"packages":[{{Entry("../x", "\"*\"")}},{{Entry("b.two", "\"*\"")}},{{Entry("b.two", "\"*\"")}},{{Entry("c", "")}},42]}""");

        await Assert.That(index!.Entries.Select(e => e.Id)).IsEquivalentTo(new[] { "b.two" });
        await Assert.That(index.Dropped).IsEqualTo(4);
    }

    [Test]
    public async Task NotAnIndexIsSaidSo()
    {
        await Assert.That(PackageIndex.Parse("not json").Problem).IsNotNull();
        await Assert.That(PackageIndex.Parse("{}").Problem).IsNotNull();
    }

    [Test]
    public async Task TheWorldsOwnPackagesComeFirstAndAnEverywherePackageDoesNotCount()
    {
        var (index, _) = PackageIndex.Parse($$"""
            {"packages":[{{Entry("all", "\"*\"")}},{{Entry("other", "\"other.example\"")}},{{Entry("mine", "\"aetherfall.mux:4201\"")}}]}
            """);

        await Assert.That(index!.For("aetherfall.mux", 4201).Select(e => e.Id)).IsEquivalentTo(new[] { "mine", "all", "other" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(index.For(null, 0).Select(e => e.Id)).IsEquivalentTo(new[] { "all", "mine", "other" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task VerifyChecksTheHashThenTheIdAndVersion()
    {
        var archive = PackageTests.Basics();
        var (index, _) = PackageIndex.Parse($$"""{"packages":[{{Entry("aether.basics", "\"*\"", sha: Hash(archive))}}]}""");
        var entry = index!.Entries.Single();
        var inside = PackageArchive.Read(archive).Contents!.Manifest;

        await Assert.That(entry.Verify(archive, inside)).IsNull();
        await Assert.That(entry.Verify([.. archive, 0], null)).Contains("SHA-256");
        await Assert.That((entry with { Version = "2.0.0" }).Verify(archive, inside)).Contains("not aether.basics 2.0.0");
    }
}
