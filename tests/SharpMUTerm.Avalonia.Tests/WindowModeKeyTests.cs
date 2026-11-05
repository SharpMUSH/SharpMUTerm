using Avalonia.Controls;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>F11 and ⇧F11 are the window's, and on Linux the window draws no decoration margin of its own.</summary>
public class WindowModeKeyTests
{
    private static ConsoleKeyInfo Key(ConsoleKey key, bool shift = false, bool control = false) =>
        new('\0', key, shift, alt: false, control);

    [Test]
    public async Task F11IsFullScreenAndShiftF11IsWindowedFullScreen()
    {
        await Assert.That(TerminalView.WindowModeFor(Key(ConsoleKey.F11))).IsEqualTo(WindowMode.FullScreen);
        await Assert.That(TerminalView.WindowModeFor(Key(ConsoleKey.F11, shift: true))).IsEqualTo(WindowMode.WindowedFullScreen);
        await Assert.That(TerminalView.WindowModeFor(Key(ConsoleKey.F11, control: true))).IsNull();
        await Assert.That(TerminalView.WindowModeFor(Key(ConsoleKey.F10))).IsNull();
    }

    [Test]
    public async Task BothChordsAreReservedSoF4AndThePaletteSayTheWindowKeepsThem()
    {
        var reserved = WindowHost.Keys.Reserved.Select(r => r.Chord).ToArray();
        await Assert.That(reserved).Contains(WindowHost.FullScreen.Chord);
        await Assert.That(reserved).Contains(WindowHost.WindowedFullScreen.Chord);
    }

    [Test]
    public async Task OnLinuxTheWindowHasNoDecorationsAvaloniaWouldDrawInsideIt()
    {
        var expected = OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD() ? WindowDecorations.None : WindowDecorations.BorderOnly;
        await Assert.That(MainWindow.DecorationsFor(titleBar: false)).IsEqualTo(expected);
        await Assert.That(MainWindow.DecorationsFor(titleBar: true)).IsEqualTo(WindowDecorations.Full);
    }
}
