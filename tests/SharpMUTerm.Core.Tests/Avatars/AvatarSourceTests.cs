using SharpMUTerm.Core.Avatars;

namespace SharpMUTerm.Core.Tests.Avatars;

/// <summary>What a picture's address may be (<see cref="AvatarSource"/>): a file on this machine or an https URL, nothing else.</summary>
public class AvatarSourceTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task BlankMeansNoPicture(string? source) =>
        await Assert.That(AvatarSource.Problem(source)).IsNull();

    [Test]
    [Arguments("https://example.com/me.png")]
    [Arguments("https://example.com/avatar?size=64")]
    public async Task AnHttpsAddressIsAccepted(string source) =>
        await Assert.That(AvatarSource.Problem(source)).IsNull();

    [Test]
    [Arguments("http://example.com/me.png")]
    [Arguments("file:///etc/passwd")]
    [Arguments("ftp://example.com/me.png")]
    [Arguments("data:image/png;base64,AAAA")]
    [Arguments("https://user:secret@example.com/me.png")]
    [Arguments("pictures/me.png")]
    [Arguments("../me.png")]
    [Arguments("me.png")]
    public async Task AnythingElseIsRefused(string source) =>
        await Assert.That(AvatarSource.Problem(source)).IsNotNull();

    [Test]
    public async Task AFileMustExistAndBeAPicture()
    {
        var folder = Directory.CreateTempSubdirectory("avatar-");
        try
        {
            var png = Path.Combine(folder.FullName, "me.png");
            var text = Path.Combine(folder.FullName, "me.txt");
            await File.WriteAllBytesAsync(png, [1, 2, 3]);
            await File.WriteAllTextAsync(text, "not a picture");

            await Assert.That(AvatarSource.Problem(png)).IsNull();
            await Assert.That(AvatarSource.Problem(text)).IsNotNull();
            await Assert.That(AvatarSource.Problem(Path.Combine(folder.FullName, "gone.png"))).IsEqualTo("there is no file there");
            await Assert.That(await AvatarSource.LoadAsync(png, http: null, CancellationToken.None)).IsEquivalentTo(new byte[] { 1, 2, 3 });
            await Assert.That(await AvatarSource.LoadAsync(text, http: null, CancellationToken.None)).IsNull();
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ControlCharactersAndOverlongAddressesAreRefused()
    {
        await Assert.That(AvatarSource.Problem("https://example.com/a\u0007.png")).IsNotNull();
        await Assert.That(AvatarSource.Problem("https://example.com/" + new string('a', AvatarSource.MaxLength))).IsNotNull();
    }

    [Test]
    public async Task AWebPictureIsNotFetchedWithoutAClient() =>
        await Assert.That(await AvatarSource.LoadAsync("https://example.com/me.png", http: null, CancellationToken.None)).IsNull();

    /// <summary>
    /// A photo straight off a phone is larger than a download may be. It is read (it is cut down to a copy), and only a
    /// file past <see cref="AvatarSource.MaxFileBytes"/> is refused, out loud.
    /// </summary>
    [Test]
    public async Task APhotoLargerThanADownloadIsReadAndOnlyAHugeFileIsRefused()
    {
        var folder = Directory.CreateTempSubdirectory("avatar-size-");
        try
        {
            var photo = Path.Combine(folder.FullName, "photo.jpg");
            using (var file = File.Create(photo))
            {
                file.SetLength(AvatarSource.MaxBytes * 2L);
            }

            var huge = Path.Combine(folder.FullName, "huge.png");
            using (var file = File.Create(huge))
            {
                file.SetLength(AvatarSource.MaxFileBytes + 1L);
            }

            await Assert.That(AvatarSource.Problem(photo)).IsNull();
            await Assert.That((await AvatarSource.LoadAsync(photo, http: null, CancellationToken.None))?.Length).IsEqualTo(AvatarSource.MaxBytes * 2);
            await Assert.That(AvatarSource.Problem(huge)).Contains("at most 32 MB");
            await Assert.That(await AvatarSource.LoadAsync(huge, http: null, CancellationToken.None)).IsNull();
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
