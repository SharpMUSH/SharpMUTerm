using SharpConsoleUI.Drivers;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// F5's world fields run past the fold on an ordinary terminal, and the field being typed in has to be
/// on screen. The cursor band stays on the WORLDS row while ⇥ walks the fields in the column beside it,
/// so the column is windowed by the open edit's caret, not by the band (<see cref="ScreenChrome.Window"/>).
/// Read off the painted frame, because the defect was a field edited where nobody could see it.
/// </summary>
[NotInParallel]
public class WorldFieldScrollTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    /// <summary>Fields whose row is labelled shorter than the field's own name.</summary>
    // The cell column draws every world field under its declared label (no shortened row labels), so the label the
    // model reports is the label to look for.
    private static readonly Dictionary<string, string> RowLabels = new();

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    [Test]
    [Arguments(120, 32)]
    [Arguments(80, 24)]
    public async Task EveryWorldFieldIsOnScreenWhileItIsBeingEdited(int width, int height)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(width, height));
        app.RenderSnapshot("worlds");

        // ⏎ on the world's row opens its first field; ⇥ walks the rest.
        app.SimulateSettingsKey(Key(ConsoleKey.Enter));
        var seen = new HashSet<int>();
        var offScreen = new List<string>();
        for (var step = 0; step < 60; step++)
        {
            var focus = app.Settings.Focus;
            if (focus?.Edit is not { } edit || !seen.Add(edit.Field))
            {
                break;
            }

            var model = WorldsScreenRenderer.Model(config.Worlds, config.TriggerSets, focus.Value.Index, 0);
            var label = model.FieldAt(focus.Value.Pane, focus.Value.Index, edit.Field)?.Label;
            label = label is not null && RowLabels.TryGetValue(label, out var shown) ? shown : label;
            var frame = FrameGrid.Decode(app.RenderWholeFrame(), width, height);
            if (label is not null && !frame.Any(row => row.Contains(label + "  ", StringComparison.Ordinal)))
            {
                offScreen.Add(label);
            }

            app.SimulateSettingsKey(Key(ConsoleKey.Tab));
        }

        await Assert.That(seen.Count).IsGreaterThan(10);
        await Assert.That(offScreen).IsEmpty();
    }
}
