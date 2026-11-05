using Avalonia.Controls;
using Avalonia.Media;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window's script editor: a script's lines are numbered in a gutter beside the native editor, level with the text,
/// and the line the reload's error named (<see cref="ComposerEdit.ErrorLine"/>, the field the cell editor marks) is the one
/// the gutter flags. A post has no gutter.
/// </summary>
[NotInParallel]
public class NativeScriptEditorTests
{
    private static ComposerModel Script(int errorLine) =>
        ComposerModel.Create(
            ComposerPlace.FullScreen,
            "Corvid",
            SharpMUTermApp.DemoScriptText,
            ComposeEscaping.AsTyped,
            new ComposerEdit("corvid.lua", "lua", Script: true, Status: errorLine > 0 ? "! corvid.lua:(14,22-30): nil" : null, ErrorLine: errorLine),
            null,
            canSend: true,
            armed: true,
            revision: 1);

    private static Task<T> Shown<T>(ComposerModel model, Func<ComposerView, T> read) =>
        DesktopLayerTests.Run((window, view, layer, driver) =>
        {
            var composer = new ComposerView(view, fullScreen: true);
            var host = new Window { Content = composer, Width = 900, Height = 700 };
            host.Show();
            composer.Show(model, new ComposerSession(model, null, new ComposerCallbacks()), new PanelInk("#5fafff", "#ff5f5f", "#3a3a40"), Colors.Black, Colors.White);
            DesktopLayerTests.Drain();
            host.UpdateLayout();
            DesktopLayerTests.Drain();
            try
            {
                return read(composer);
            }
            finally
            {
                host.Close();
            }
        });

    [Test]
    public async Task AScriptsLinesAreNumberedAndTheErrorLineIsFlagged()
    {
        var (visible, width, numbers, editorLine) = await Shown(Script(14), c =>
            (c.Gutter.IsVisible, c.Gutter.Bounds.Width, c.Gutter.Numbers, c.Editor.ErrorLine));

        await Assert.That(visible).IsTrue();
        await Assert.That(width).IsGreaterThan(0);
        await Assert.That(numbers.Select(n => n.Line).Take(3)).IsEquivalentTo([1, 2, 3], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(numbers.Where(n => n.Error).Select(n => n.Line)).IsEquivalentTo([14]);
        await Assert.That(editorLine).IsEqualTo(14).Because("the band behind the text is drawn from the same field");
    }

    [Test]
    public async Task ACleanScriptFlagsNothingAndAPostHasNoGutter()
    {
        var clean = await Shown(Script(0), c => c.Gutter.Numbers.Count(n => n.Error));
        await Assert.That(clean).IsEqualTo(0);

        var post = ComposerModel.Create(ComposerPlace.FullScreen, "Corvid", "hello", ComposeEscaping.AsTyped, null, null, true, true, 1);
        var gutter = await Shown(post, c => c.Gutter.IsVisible);
        await Assert.That(gutter).IsFalse();
    }
}
