using SharpMUTerm.Core.Avatars;

namespace SharpMUTerm.Core.Tests.Avatars;

/// <summary>
/// The crop a picture is kept at: the square in pixels, the window's stage that chooses it, and the folder of copies.
/// </summary>
public class AvatarCropTests
{
    [Test]
    public async Task TheCentredCropIsTheLargestCentredSquare()
    {
        await Assert.That(AvatarCrop.Centre.In(200, 100)).IsEqualTo((50, 0, 100));
        await Assert.That(AvatarCrop.Centre.In(100, 300)).IsEqualTo((0, 100, 100));
    }

    [Test]
    public async Task ACropIsMovedIntoThePictureNeverShrunk()
    {
        await Assert.That(new AvatarCrop(1, 1, 0.5).In(200, 100)).IsEqualTo((150, 50, 50));
        await Assert.That(new AvatarCrop(-3, 0.5, 2).In(200, 100)).IsEqualTo((0, 0, 100));
        await Assert.That(new AvatarCrop(double.NaN, double.NaN, double.NaN).In(200, 100)).IsEqualTo((50, 0, 100));
    }

    [Test]
    public async Task TheStageOpensOnTheCentredCrop()
    {
        var stage = new AvatarCropStage(400, 200, 100);
        await Assert.That(stage.Zoom).IsEqualTo(1);
        await Assert.That(stage.Top).IsEqualTo(0);
        await Assert.That(stage.Left).IsEqualTo(-50);
        await Assert.That(stage.Crop.In(400, 200)).IsEqualTo(AvatarCrop.Centre.In(400, 200));
    }

    [Test]
    public async Task ThePictureCannotBeDraggedOffTheCircle()
    {
        var stage = new AvatarCropStage(400, 200, 100);
        stage.Pan(500, 500);
        await Assert.That(stage.Left).IsEqualTo(0);
        await Assert.That(stage.Top).IsEqualTo(0);
        await Assert.That(stage.Crop.In(400, 200)).IsEqualTo((0, 0, 200));

        stage.Pan(-5000, -5000);
        await Assert.That(stage.Left).IsEqualTo(-100);
        await Assert.That(stage.Crop.In(400, 200)).IsEqualTo((200, 0, 200));
    }

    [Test]
    public async Task ZoomingKeepsTheCentreAndNarrowsTheSquare()
    {
        var stage = new AvatarCropStage(200, 200, 100);
        stage.ZoomTo(2);
        await Assert.That(stage.Crop.In(200, 200)).IsEqualTo((50, 50, 100));

        stage.Pan(-50, 0); // at twice the size a stage pixel is a picture pixel: the square moves 50 to the right
        await Assert.That(stage.Crop.In(200, 200)).IsEqualTo((100, 50, 100));

        stage.ZoomTo(99);
        await Assert.That(stage.Zoom).IsEqualTo(AvatarCropStage.MaxZoom);
        stage.ZoomTo(0);
        await Assert.That(stage.Zoom).IsEqualTo(1);
        await Assert.That(stage.Crop.In(200, 200)).IsEqualTo((0, 0, 200));
    }

    [Test]
    public async Task ACopyIsSavedPrunedAndKnownForOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "avatar-lib-" + Guid.NewGuid().ToString("N"));
        try
        {
            var library = new AvatarLibrary(root);
            var kept = library.Save(PngWriter.Rgba(1, 1, [1, 2, 3, 255]));
            var dropped = library.Save(PngWriter.Rgba(1, 1, [4, 5, 6, 255]));

            await Assert.That(kept).IsNotNull();
            await Assert.That(File.Exists(kept!)).IsTrue();
            await Assert.That(library.IsCopy(kept)).IsTrue();
            await Assert.That(library.IsCopy("/elsewhere/avatar-x.png")).IsFalse();
            await Assert.That(AvatarSource.Problem(kept)).IsNull();

            library.Prune([kept, null, "https://example.com/a.png"]);
            await Assert.That(File.Exists(kept)).IsTrue();
            await Assert.That(File.Exists(dropped!)).IsFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task WithNoFolderNothingIsSaved()
    {
        var library = new AvatarLibrary();
        await Assert.That(library.Save(PngWriter.Rgba(1, 1, [1, 2, 3, 255]))).IsNull();
        await Assert.That(library.IsCopy("/anything/avatar-1.png")).IsFalse();
    }
}
