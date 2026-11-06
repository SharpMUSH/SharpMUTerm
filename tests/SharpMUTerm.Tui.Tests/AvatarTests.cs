using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Avatars;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Characters' and worlds' pictures on the sidebar: read only with a reader, the character's own first, then its
/// world's, then the letter; and on the cell rail a picture takes exactly the cells the dot did, so no width moves.
/// </summary>
public class AvatarTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly TerminalColor Accent = TerminalColor.FromRgb(0x00, 0xf5, 0xb7);

    private static readonly AvatarPicture Picture = new("/p.png", AvatarStore.Size, new byte[AvatarStore.Size * AvatarStore.Size * 4]);

    private static IReadOnlyList<RailRow> Rows(AvatarPicture? corvid, AvatarPicture? rookery) => RailModel.Build(new[]
    {
        new RailWorld("Aetherfall", "aether.example", 4000, Accent, new[]
        {
            new RailCharacter("Corvid", "s1", Connected: true, Active: false, Unread: 0, Array.Empty<RailWindow>(), Avatar: corvid),
            new RailCharacter("Rookery", "s2", Connected: false, Active: false, Unread: 0, Array.Empty<RailWindow>(), Avatar: rookery),
        }),
    });

    [Test]
    public async Task WithNoReaderNothingIsRead()
    {
        using var store = new AvatarStore(_ => true, () => { });
        await Assert.That(store.Get(DemoAvatars.For("corvid"))).IsNull();
    }

    [Test]
    public async Task APictureIsCutToACentredSquareAndKeptAsOneInstance()
    {
        using var store = new AvatarStore(_ => true, () => { }) { Reader = DemoAvatars.ReadAsync };
        var first = store.Get(DemoAvatars.For("corvid"));
        var again = store.Get(DemoAvatars.For("corvid"));

        await Assert.That(first).IsNotNull();
        await Assert.That(first!.Size).IsEqualTo(AvatarStore.Size);
        await Assert.That(first.Rgba.Length).IsEqualTo(AvatarStore.Size * AvatarStore.Size * 4);
        await Assert.That(ReferenceEquals(first, again)).IsTrue();
        await Assert.That(store.Get(DemoAvatars.For("nobody"))).IsNull();
        await Assert.That(AvatarStore.Picture("x", [1, 2, 3])).IsNull();
    }

    [Test]
    public async Task APictureTakesTheDotsCellsAndNoMore()
    {
        var plain = RailRenderer.Render(Rows(null, null));
        var pictured = RailRenderer.Render(Rows(Picture, Picture), avatars: (_, connected) => connected ? 0xF00000 : 0xF00001);
        var placeholder = char.ConvertFromUtf32(KittyGraphicsProtocol.PlaceholderCodePoint);

        var corvid = pictured.Single(l => l.Contains("Corvid"));
        await Assert.That(corvid).Contains(placeholder);
        await Assert.That(corvid).DoesNotContain("●");
        for (var i = 0; i < plain.Count; i++)
        {
            await Assert.That(SharpMUTermApp.MarkupWidth(pictured[i])).IsEqualTo(SharpMUTermApp.MarkupWidth(plain[i]));
        }

        var folded = RailRenderer.RenderCollapsed(Rows(null, null));
        var foldedPictured = RailRenderer.RenderCollapsed(Rows(Picture, null), avatars: (_, _) => 0xF00000);
        await Assert.That(foldedPictured.Max(SharpMUTermApp.MarkupWidth)).IsEqualTo(folded.Max(SharpMUTermApp.MarkupWidth));
    }

    [Test]
    public async Task WhereNothingCanBeSentTheLetterStays()
    {
        var lines = RailRenderer.Render(Rows(Picture, Picture), avatars: (_, _) => null);
        await Assert.That(string.Concat(lines)).DoesNotContain(char.ConvertFromUtf32(KittyGraphicsProtocol.PlaceholderCodePoint));
        await Assert.That(lines.Single(l => l.Contains("Corvid"))).Contains("●");
    }

    [Test]
    public async Task ACharacterWearsItsOwnPictureThenItsWorldsThenNone()
    {
        var config = DemoScene.Build();
        var aetherfall = config.Worlds.Single(w => w.Name == "Aetherfall");
        aetherfall.Avatar = DemoAvatars.For("aetherfall");
        aetherfall.Characters.Single(c => c.Name == "Corvid").Avatar = DemoAvatars.For("corvid");
        await using var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32)) { AvatarFetch = DemoAvatars.ReadAsync };

        var sidebar = app.BuildSidebar();
        var characters = sidebar.Worlds.SelectMany(w => w.Characters).ToDictionary(c => c.Name);
        var world = sidebar.Worlds.Single(w => w.Name == "Aetherfall");

        await Assert.That(characters["Corvid"].Avatar!.Source).IsEqualTo(DemoAvatars.For("corvid"));
        await Assert.That(ReferenceEquals(characters["Rookery"].Avatar, world.Avatar)).IsTrue();
        await Assert.That(world.Avatar).IsNotNull();
        await Assert.That(characters["Thistle"].Avatar).IsNull();
        await Assert.That(app.BuildSidebar()).IsEqualTo(sidebar);
    }
}

/// <summary>
/// The copy kept of a picture: written beside the configuration, the setting pointed at it, and the original no longer
/// needed — the reported "what happens when the file is deleted".
/// </summary>
public class AvatarCopyTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    [Test]
    public async Task ThePngWrittenDecodesBackToItsPixels()
    {
        var rgba = new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 9, 9, 9, 255 };
        var back = AvatarStore.Cropped(PngWriter.Rgba(2, 2, rgba), AvatarCrop.Centre, 2);
        await Assert.That(back).IsNotNull();
        await Assert.That(back!.SequenceEqual(rgba)).IsTrue();
    }

    [Test]
    public async Task APictureIsCopiedCroppedAndTheSettingPointedAtTheCopy()
    {
        var folder = Path.Combine(Path.GetTempPath(), "avatar-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // Left half red, right half blue: the crop the window chose (the right-hand square) is what is kept.
            var original = Path.Combine(folder, "wide.png");
            var pixels = new byte[4 * 2 * 4];
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    var at = ((y * 4) + x) * 4;
                    pixels[at + (x < 2 ? 0 : 2)] = 255;
                    pixels[at + 3] = 255;
                }
            }

            await File.WriteAllBytesAsync(original, PngWriter.Rgba(4, 2, pixels));
            var config = DemoScene.Build();
            var world = config.Worlds.Single(w => w.Name == "Aetherfall");
            var corvid = world.Characters.Single(c => c.Name == "Corvid");
            corvid.Avatar = original;
            world.Avatar = original;

            var library = new AvatarLibrary(Path.Combine(folder, "avatars"));
            await using var app = new SharpMUTermApp(
                config,
                ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null),
                avatarFetch: AvatarSource.LoadAsync,
                avatarCopies: library);
            app.ChooseAvatarCrop(original, new AvatarCrop(0.75, 0.5, 1));
            await app.KeepAvatarCopies();

            await Assert.That(library.IsCopy(corvid.Avatar)).IsTrue();
            await Assert.That(world.Avatar).IsEqualTo(corvid.Avatar);

            File.Delete(original);
            var kept = AvatarStore.Cropped(await File.ReadAllBytesAsync(corvid.Avatar!), AvatarCrop.Centre, 1)!;
            await Assert.That(kept[0]).IsEqualTo((byte)0);
            await Assert.That(kept[2]).IsEqualTo((byte)255);
            await Assert.That(AvatarSource.Problem(corvid.Avatar)).IsNull();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public async Task WithNoFolderTheSourceIsKeptAsTyped()
    {
        var config = DemoScene.Build();
        var corvid = config.Worlds.SelectMany(w => w.Characters).First(c => c.Name == "Corvid");
        corvid.Avatar = DemoAvatars.For("corvid");
        await using var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32)) { AvatarFetch = DemoAvatars.ReadAsync };
        await app.KeepAvatarCopies();
        await Assert.That(corvid.Avatar).IsEqualTo(DemoAvatars.For("corvid"));
    }
}
