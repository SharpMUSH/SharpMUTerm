using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Threading;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// What a screen reader finds when it reaches the cells: a read-only document named after the window in front, whose
/// value is that window's newest lines as plain text, oldest first (<see cref="WindowAnnouncer.Front"/>). The cells
/// themselves are a picture to the platform's accessibility API — a <see cref="TerminalView"/> paints with Skia and
/// has no text elements — so without this a reader moving to the output found an unnamed graphic.
/// <para>
/// It sits beside the two live regions, not in place of them: those speak each line as it arrives, this is the text a
/// reader can go back over at its own pace (UI Automation's Value pattern on Windows, AT-SPI's text on Linux). A change
/// to it raises the Value property's change, posted to Avalonia's thread, so a reader watching the view hears that it
/// changed without being read every line twice.
/// </para>
/// </summary>
internal sealed class TerminalViewAutomationPeer : ControlAutomationPeer, IValueProvider
{
    private readonly WindowAnnouncer _announcer;
    private string _value;

    public TerminalViewAutomationPeer(TerminalView owner, WindowAnnouncer announcer)
        : base(owner)
    {
        _announcer = announcer;
        _value = Text(announcer.Front);
        announcer.ReadingChanged += () => Dispatcher.UIThread.Post(Refresh);
    }

    /// <summary>The name a reader announces for the view: the window in front, after the product.</summary>
    internal static string NameFor(string? window) =>
        string.IsNullOrEmpty(window) ? "SharpMUTerm output" : $"SharpMUTerm output: {window}";

    public bool IsReadOnly => true;

    public string? Value => _value;

    /// <summary>The output is the game's; a reader may read it, never write it.</summary>
    public void SetValue(string? value) => throw new InvalidOperationException("The output is read-only.");

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => "SharpMUTermOutput";

    protected override string? GetNameCore() => NameFor(_announcer.Front.Window);

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    private void Refresh()
    {
        var old = _value;
        _value = Text(_announcer.Front);
        if (old != _value)
        {
            RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, old, _value);
        }
    }

    private static string Text((string? Window, IReadOnlyList<string> Lines) front) => string.Join('\n', front.Lines);
}
