using System.Drawing;
using SharpConsoleUI.Drivers;

namespace SharpMUTerm.Tui;

/// <summary>
/// Notices that the reader has come back to the terminal after being away from it, so the client can
/// mark where they were (see <see cref="AwayBarRenderer"/>).
/// <para>
/// <b>This whole type is a workaround, and it is one file so that it is one file to delete.</b> A
/// terminal reports focus only if asked — <c>CSI ?1004h</c> turns it on, after which the terminal writes
/// <c>ESC [ I</c> on focus gain and <c>ESC [ O</c> on focus loss, down the same pipe as keystrokes.
/// SharpConsoleUI neither asks nor decodes: no released version emits <c>?1004</c> (checked against
/// 2.5.18, the newest published; we are pinned at 2.5.14), <c>IConsoleDriver</c> carries no focus event,
/// and <c>NetConsoleDriverOptions</c> no hook. The input-stack wall in CLAUDE.md is real — owning this
/// properly means an upstream PR or a from-scratch driver.
/// </para>
/// <para>
/// <b>How the two halves get in.</b> <see cref="IConsoleDriver.WriteClipboardOsc52"/> is named for its
/// first customer and does not do what its name says: its body takes the console lock and writes the
/// string verbatim, with no validation or wrapping. It is a raw-escape emitter, and it is the only
/// public write serialised against the renderer — the framework paints whole frames through stdout, so
/// a <c>Console.Out.Write</c> of our own could land mid-frame. Emissions go through
/// <see cref="EmitTerminalMode"/> alone, so if a future version starts validating that payload there is
/// one line to change.
/// </para>
/// <para>
/// And focus-in arrives <em>disguised as a Tab keypress</em>: <c>AnsiInputParser.DispatchCsi</c> reads a
/// trailing <c>I</c> as Tab, which is right for the forms carrying modifiers (<c>ESC [ 1;5 I</c> is
/// genuinely Ctrl+Tab in xterm) and wrong for the bare form, which is focus-in. So this type does not
/// choose to concern itself with Tab; Tab is the shape the message arrives in.
/// </para>
/// <para>
/// <b>Focus-out is not recoverable.</b> <c>ESC [ O</c> has no case in <c>DispatchCsi</c> and becomes an
/// <c>UnknownSequenceEvent</c>, which <c>UnixStdinReader</c> never dispatches. So we cannot timestamp a
/// departure, and <see cref="Returned"/> reports the gap since the last input event instead — you stop
/// typing, then you leave, so it is seconds long at worst.
/// </para>
/// </summary>
internal sealed class TerminalFocusWatcher : IDisposable
{
    /// <summary>Turns focus reporting on.</summary>
    private const string EnableFocusReporting = "\x1b[?1004h";

    /// <summary>Turns focus reporting off again.</summary>
    private const string DisableFocusReporting = "\x1b[?1004l";

    /// <summary>
    /// How long a quiet gap has to be before an arriving Tab is read as a return rather than as a Tab.
    /// <para>
    /// This is a debounce and not an idle timer: nothing is drawn without an actual focus-in, so the
    /// number only has to be longer than the pauses inside ordinary typing. It can therefore be short,
    /// and short is what keeps a real Tab a real Tab. The misfire is benign in the direction it fires —
    /// a genuine Tab pressed after half a minute of silence is <em>also</em> a return, because you had
    /// to come back to the terminal to press it. <c>Ctrl+I</c>, which the terminal spells as a bare Tab
    /// as well (see <see cref="MacroKeys"/>), is covered by the same test.
    /// </para>
    /// </summary>
    public static readonly TimeSpan DefaultReturnThreshold = TimeSpan.FromSeconds(30);

    private readonly IConsoleDriver _driver;
    private readonly TimeProvider _time;
    private readonly TimeSpan _threshold;
    private readonly bool _enabled;

    private DateTimeOffset _lastInputAt;
    private DateTimeOffset _previousInputAt;
    private bool _started;
    private bool _disposed;

    public TerminalFocusWatcher(
        IConsoleDriver driver,
        TimeProvider time,
        bool enabled,
        TimeSpan? threshold = null)
    {
        _driver = driver;
        _time = time;
        _enabled = enabled;
        _threshold = threshold ?? DefaultReturnThreshold;
        _lastInputAt = _previousInputAt = time.GetUtcNow();
    }

    /// <summary>
    /// Raised when the reader has come back, carrying how long they were away for. Runs on whichever
    /// thread delivered the Tab, which is the UI thread: a global shortcut is dispatched from the
    /// framework's own input pump.
    /// </summary>
    public event Action<TimeSpan>? Returned;

    /// <summary>
    /// Raised for every input event the driver saw — keys, mouse and paste alike, and including input
    /// routed to an overlay rather than to the workspace. It is what the client hangs "where was the
    /// reader last looking" off, and it is here rather than on the app's own key handler because that
    /// handler does not see a key an overlay consumed.
    /// </summary>
    public event Action? Input;

    /// <summary>
    /// Whether this watcher will do anything at all. False leaves the terminal untouched and leaves
    /// every Tab alone, which is what <see cref="ShouldEnable"/> asks for off Unix.
    /// </summary>
    public bool IsEnabled => _enabled;

    /// <summary>
    /// Whether focus reporting can be used with <paramref name="driver"/> at all.
    /// <para>
    /// <b>Not on Windows.</b> The Windows branch of <c>NetConsoleDriver</c> is a
    /// <c>Console.ReadKey</c> loop with its own ad-hoc sequence reassembly rather than
    /// <c>AnsiInputParser</c>, so an <c>ESC [ I</c> arriving there would be reassembled into something
    /// else entirely. The feature is inert on Windows, deliberately, rather than wrong.
    /// </para>
    /// <para>
    /// <b>Not headless.</b> A snapshot or a test has no terminal to report focus and no reader to have
    /// left one. <c>WriteClipboardOsc52</c> is a default interface method with an empty body, so the
    /// emission would already be a no-op there — but the <em>Tab interpretation</em> would not be, and a
    /// headless harness pressing Tab must get a Tab.
    /// </para>
    /// </summary>
    public static bool ShouldEnable(IConsoleDriver driver) =>
        !OperatingSystem.IsWindows() && driver is not HeadlessConsoleDriver;

    /// <summary>Asks the terminal to report focus and starts listening for input. Idempotent.</summary>
    public void Start()
    {
        if (!_enabled || _started)
        {
            return;
        }

        _started = true;
        _driver.KeyPressed += OnKeyPressed;
        _driver.Paste += OnPaste;
        _driver.MouseEvent += OnMouse;
        EmitTerminalMode(EnableFocusReporting);
    }

    /// <summary>
    /// Stops the terminal reporting focus and stops listening. Idempotent, and safe to call on a
    /// watcher that never started — which is what a disposal after a failed launch does.
    /// </summary>
    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _driver.KeyPressed -= OnKeyPressed;
        _driver.Paste -= OnPaste;
        _driver.MouseEvent -= OnMouse;
        EmitTerminalMode(DisableFocusReporting);
    }

    /// <summary>
    /// Offers this watcher a bare Tab, and reports whether it was the terminal's focus-in rather than
    /// the reader's Tab key. True means the key is consumed and <see cref="Returned"/> has been raised;
    /// false means it is an ordinary Tab and must carry on down the pipeline untouched.
    /// <para>
    /// <b>The comparison is against the input before this one.</b> The disguised focus-in <em>is</em> a
    /// <c>KeyPressed</c>, and the driver raises that before <c>InputCoordinator</c> reaches the global
    /// shortcuts — so by the time this runs, <see cref="_lastInputAt"/> has already been moved to now by
    /// this very keystroke. Measuring from it would find a gap of zero every time and this would never
    /// fire once.
    /// </para>
    /// </summary>
    public bool TryTakeAsReturn()
    {
        if (!_enabled)
        {
            return false;
        }

        var away = _lastInputAt - _previousInputAt;
        if (away < _threshold)
        {
            return false;
        }

        Returned?.Invoke(away);
        return true;
    }

    /// <summary>
    /// Records that the reader did something. Public because a headless harness drives keys straight
    /// into the app rather than through a driver that could raise them (see <c>SimulateKey</c>), and the
    /// two paths must reach the same state or a test would be exercising a different rule from the one
    /// that ships.
    /// </summary>
    public void NoteInput()
    {
        _previousInputAt = _lastInputAt;
        _lastInputAt = _time.GetUtcNow();
        Input?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void OnKeyPressed(object? sender, ConsoleKeyInfo key) => NoteInput();

    private void OnPaste(object? sender, string text) => NoteInput();

    private void OnMouse(object sender, List<MouseFlags> flags, Point point) => NoteInput();

    /// <summary>
    /// The one place a terminal mode is written. See the type remarks for why this goes through the
    /// clipboard-named writer and why it is worth having exactly one line that does.
    /// </summary>
    private void EmitTerminalMode(string sequence) => _driver.WriteClipboardOsc52(sequence);
}
