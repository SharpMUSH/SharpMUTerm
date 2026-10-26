using SharpMUTerm.Core.Media;

namespace SharpMUTerm.Core.Tests.Media;

public class MediaCacheTests
{
    [Test]
    [Arguments("../secrets.wav")]
    [Arguments("/etc/passwd.wav")]
    [Arguments("C:/x.wav")]
    [Arguments("a\\b.wav")]
    [Arguments("sounds//x.wav")]
    [Arguments("run.exe")]
    [Arguments("")]
    public async Task AServersNameMustBeARelativeSoundFile(string name)
    {
        await Assert.That(MediaCache.CheckName(name)).IsNotNull();
    }

    [Test]
    [Arguments("rain.ogg")]
    [Arguments("weather/Thunder.WAV")]
    public async Task OrdinaryNamesAreAccepted(string name)
    {
        await Assert.That(MediaCache.CheckName(name)).IsNull();
    }

    [Test]
    public async Task OnlyHttpAndHttpsFoldersAreFetchedFrom()
    {
        await Assert.That(MediaCache.UrlFor("a b.ogg", "https://x.example/s")!.AbsoluteUri).IsEqualTo("https://x.example/s/a%20b.ogg");
        await Assert.That(MediaCache.UrlFor("a.ogg", "file:///etc/")).IsNull();
        await Assert.That(MediaCache.UrlFor("a.ogg", null)).IsNull();
    }

    [Test]
    public async Task ACacheWithNoRootFetchesNothing()
    {
        var fetched = false;
        var cache = new MediaCache(fetch: (_, _, _) => { fetched = true; return Task.FromResult<byte[]?>([1]); });

        var (path, problem) = await cache.ResolveAsync("w", "rain.ogg", "https://x.example/");

        await Assert.That(path).IsNull();
        await Assert.That(problem).IsNotNull();
        await Assert.That(fetched).IsFalse();
    }

    [Test]
    public async Task AFileIsFetchedOnceAndThenReadFromDisk()
    {
        var root = Directory.CreateTempSubdirectory("smt-cache-").FullName;
        var fetches = 0;
        var cache = new MediaCache(root, (_, _, _) => { fetches++; return Task.FromResult<byte[]?>([1, 2, 3]); });

        var first = await cache.ResolveAsync("game.example:4000", "rain.ogg", "https://x.example/");
        var second = await cache.ResolveAsync("game.example:4000", "rain.ogg", "https://x.example/");

        await Assert.That(first.Path).IsEqualTo(second.Path);
        await Assert.That(fetches).IsEqualTo(1);
        await Assert.That(first.Path!.StartsWith(root, StringComparison.Ordinal)).IsTrue();
        await Assert.That(Path.GetFileName(first.Path)).DoesNotContain("rain");
    }

    [Test]
    public async Task DownloadingOffMeansOnlyWhatIsAlreadyHere()
    {
        var root = Directory.CreateTempSubdirectory("smt-cache-").FullName;
        var cache = new MediaCache(root, (_, _, _) => Task.FromResult<byte[]?>([1]));

        var (path, problem) = await cache.ResolveAsync("w", "rain.ogg", "https://x.example/", download: false);

        await Assert.That(path).IsNull();
        await Assert.That(problem).IsEqualTo("downloading is off for this world");
    }

    [Test]
    public async Task AWorldsCacheIsCappedOldestFirst()
    {
        var root = Directory.CreateTempSubdirectory("smt-cache-").FullName;
        var cache = new MediaCache(root, (_, _, _) => Task.FromResult<byte[]?>(new byte[60])) { MaxWorldBytes = 100 };

        var a = await cache.ResolveAsync("w", "a.ogg", "https://x.example/");
        File.SetLastWriteTimeUtc(a.Path!, DateTime.UtcNow.AddMinutes(-5));
        var b = await cache.ResolveAsync("w", "b.ogg", "https://x.example/");

        await Assert.That(File.Exists(a.Path!)).IsFalse();
        await Assert.That(File.Exists(b.Path!)).IsTrue();
    }

    [Test]
    public async Task AnOversizedFileIsRefused()
    {
        var root = Directory.CreateTempSubdirectory("smt-cache-").FullName;
        var cache = new MediaCache(root, (_, _, _) => Task.FromResult<byte[]?>(new byte[11])) { MaxFileBytes = 10 };

        var (path, _) = await cache.ResolveAsync("w", "a.ogg", "https://x.example/");

        await Assert.That(path).IsNull();
        await Assert.That(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)).IsEmpty();
    }

    [Test]
    public async Task ASoundpackCannotBeEscapedByName()
    {
        var pack = Directory.CreateTempSubdirectory("smt-pack-").FullName;
        File.WriteAllBytes(Path.Combine(pack, "rain.ogg"), [1]);
        var cache = new MediaCache();
        cache.LocalFolders.Add(pack);

        await Assert.That((await cache.ResolveAsync("w", "rain.ogg", null)).Path).IsEqualTo(Path.Combine(pack, "rain.ogg"));
        await Assert.That((await cache.ResolveAsync("w", "../rain.ogg", null)).Path).IsNull();
    }
}
