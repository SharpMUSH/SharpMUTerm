using SharpConsoleUI.Drivers;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The rule that tells the terminal's focus report apart from the reader's Tab key.
/// <para>
/// It is a rule about <em>time</em> and not about the key, because the key is indistinguishable:
/// SharpConsoleUI's parser turns the bare <c>ESC [ I</c> a terminal writes on focus-in into a
/// <c>ConsoleKey.Tab</c>, identical in every field to a Tab the reader pressed. See
/// <see cref="TerminalFocusWatcher"/> for why that is the only channel available.
/// </para>
/// </summary>
public class TerminalFocusWatcherTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(30);

    [Test]
    public async Task ATabWhileTheReaderIsAtTheKeyboardIsAnOrdinaryTab()
    {
        var (watcher, time, returns) = Watcher();

        watcher.NoteInput();
        time.Advance(TimeSpan.FromSeconds(2));
        watcher.NoteInput(); // the Tab's own KeyPressed, which the driver raises first

        await Assert.That(watcher.TryTakeAsReturn()).IsFalse();
        await Assert.That(returns).IsEmpty();
    }

    [Test]
    public async Task ATabAfterAQuietGapIsTheTerminalReportingFocus()
    {
        var (watcher, time, returns) = Watcher();

        watcher.NoteInput();
        time.Advance(TimeSpan.FromMinutes(12));
        watcher.NoteInput();

        await Assert.That(watcher.TryTakeAsReturn()).IsTrue();
        await Assert.That(returns).Count().IsEqualTo(1);
        await Assert.That(returns[0]).IsEqualTo(TimeSpan.FromMinutes(12));
    }

    /// <summary>
    /// The ordering trap, pinned. The disguised focus-in <em>is</em> a <c>KeyPressed</c> and the driver
    /// raises that before <c>InputCoordinator</c> reaches the global shortcuts, so the watcher must
    /// measure from the input <em>before</em> the Tab. Measuring from the latest one finds a gap of zero
    /// on every return and the feature never fires at all — which is a bug that looks exactly like the
    /// terminal not supporting focus reporting.
    /// </summary>
    [Test]
    public async Task TheGapIsMeasuredFromTheInputBeforeTheTabsOwn()
    {
        var (watcher, time, _) = Watcher();

        watcher.NoteInput();
        time.Advance(TimeSpan.FromMinutes(12));
        watcher.NoteInput();

        // No time passes between the Tab's KeyPressed and the shortcut running: a watcher reading the
        // latest timestamp would see nothing at all here.
        await Assert.That(watcher.TryTakeAsReturn()).IsTrue();
    }

    [Test]
    public async Task ASecondTabStraightAfterAReturnIsAnOrdinaryTab()
    {
        var (watcher, time, returns) = Watcher();

        watcher.NoteInput();
        time.Advance(TimeSpan.FromMinutes(12));
        watcher.NoteInput();
        await Assert.That(watcher.TryTakeAsReturn()).IsTrue();

        // You are back, and now you press Tab to cycle command bars. The baseline moved with the
        // return, so this must reach InputBarControl rather than being eaten as a second focus-in.
        time.Advance(TimeSpan.FromSeconds(1));
        watcher.NoteInput();
        await Assert.That(watcher.TryTakeAsReturn()).IsFalse();
        await Assert.That(returns).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ADisabledWatcherLeavesEveryTabAlone()
    {
        var (watcher, time, returns) = Watcher(enabled: false);

        watcher.NoteInput();
        time.Advance(TimeSpan.FromHours(3));
        watcher.NoteInput();

        await Assert.That(watcher.TryTakeAsReturn()).IsFalse();
        await Assert.That(returns).IsEmpty();
    }

    [Test]
    public async Task StartAsksTheTerminalToReportFocusAndStopTurnsItOff()
    {
        var driver = new RecordingConsoleDriver();
        using var watcher = new TerminalFocusWatcher(driver, new ManualTimeProvider(), enabled: true);

        watcher.Start();
        await Assert.That(driver.Written).IsEquivalentTo(new[] { "\x1b[?1004h" });

        watcher.Stop();
        await Assert.That(driver.Written).IsEquivalentTo(new[] { "\x1b[?1004h", "\x1b[?1004l" });
    }

    [Test]
    public async Task StartingTwiceAsksOnce()
    {
        var driver = new RecordingConsoleDriver();
        using var watcher = new TerminalFocusWatcher(driver, new ManualTimeProvider(), enabled: true);

        watcher.Start();
        watcher.Start();

        await Assert.That(driver.Written).Count().IsEqualTo(1);
    }

    /// <summary>
    /// A watcher that is off writes nothing at all. This is the same guarantee <c>save</c>, <c>logRoot</c>
    /// and <c>restore</c> carry, one layer out: an app that is not the live entry point must not reach
    /// the developer's terminal any more than it reaches their configuration.
    /// </summary>
    [Test]
    public async Task ADisabledWatcherWritesNothingToTheTerminal()
    {
        var driver = new RecordingConsoleDriver();
        using var watcher = new TerminalFocusWatcher(driver, new ManualTimeProvider(), enabled: false);

        watcher.Start();
        watcher.Stop();

        await Assert.That(driver.Written).IsEmpty();
    }

    [Test]
    public async Task StopOnAWatcherThatNeverStartedWritesNothing()
    {
        var driver = new RecordingConsoleDriver();
        var watcher = new TerminalFocusWatcher(driver, new ManualTimeProvider(), enabled: true);

        watcher.Dispose();

        await Assert.That(driver.Written).IsEmpty();
    }

    /// <summary>
    /// Headless means no terminal to report focus and no reader to have left one — and, more sharply,
    /// a harness pressing Tab must get a Tab. The emission would already be a no-op there
    /// (<c>WriteClipboardOsc52</c> is a default interface method with an empty body); the Tab
    /// interpretation would not be.
    /// </summary>
    [Test]
    public async Task ShouldEnable_IsFalseForAHeadlessDriver()
    {
        using var headless = new HeadlessConsoleDriver(80, 24);

        await Assert.That(TerminalFocusWatcher.ShouldEnable(headless)).IsFalse();
    }

    [Test]
    public async Task NoteInputAnnouncesEveryInputEvent()
    {
        var (watcher, _, _) = Watcher();
        var seen = 0;
        watcher.Input += () => seen++;

        watcher.NoteInput();
        watcher.NoteInput();

        await Assert.That(seen).IsEqualTo(2);
    }

    private static (TerminalFocusWatcher Watcher, ManualTimeProvider Time, List<TimeSpan> Returns) Watcher(
        bool enabled = true)
    {
        var time = new ManualTimeProvider();
        var watcher = new TerminalFocusWatcher(new RecordingConsoleDriver(), time, enabled, Threshold);
        var returns = new List<TimeSpan>();
        watcher.Returned += away => returns.Add(away);
        return (watcher, time, returns);
    }

    /// <summary>
    /// A headless driver that remembers what was written through the raw-escape seam. It subclasses
    /// rather than reimplementing <c>IConsoleDriver</c> from scratch, because that interface is wide and
    /// none of the rest of it matters here.
    /// <para>
    /// It names <c>IConsoleDriver</c> in its base list even though <c>HeadlessConsoleDriver</c> already
    /// does, and that is load-bearing rather than noise. <c>WriteClipboardOsc52</c> is a <em>default</em>
    /// interface method that <c>HeadlessConsoleDriver</c> does not override, so the interface mapping is
    /// fixed at the base class and a matching public method on a derived type would never be reached
    /// through an <c>IConsoleDriver</c> reference — which is exactly how the watcher holds it. Re-listing
    /// the interface is what re-runs the mapping.
    /// </para>
    /// </summary>
    private sealed class RecordingConsoleDriver : HeadlessConsoleDriver, IConsoleDriver
    {
        public RecordingConsoleDriver() : base(80, 24)
        {
        }

        public List<string> Written { get; } = new();

        public void WriteClipboardOsc52(string sequence) => Written.Add(sequence);
    }
}
