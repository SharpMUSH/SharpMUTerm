using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's half of <see cref="IAttention"/>. The main window says when it is activated and deactivated
/// (<see cref="SetFocused"/>); the client hands a request over on its own thread, and the main window, which
/// subscribes to <see cref="Requested"/>, puts it on screen on Avalonia's thread — but only while it is not the active
/// window, which is why <see cref="FiltersByFocus"/> is true.
/// </summary>
internal sealed class WindowAttention : IAttention
{
    private volatile int _focused = -1;

    public bool FiltersByFocus => true;

    public bool? Focused => _focused switch
    {
        0 => false,
        1 => true,
        _ => null,
    };

    public event Action<bool>? FocusChanged;

    /// <summary>Raised on the client's thread with each request.</summary>
    public event Action<AttentionRequest>? Requested;

    public void Request(AttentionRequest request) => Requested?.Invoke(request);

    /// <summary>Called by the main window on activation (true) and deactivation (false).</summary>
    public void SetFocused(bool focused)
    {
        var value = focused ? 1 : 0;
        if (Interlocked.Exchange(ref _focused, value) != value)
        {
            FocusChanged?.Invoke(focused);
        }
    }
}

