using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window host's <see cref="INativeModals"/>: the quit prompt, ⌃F search, ⌃R history, the ⌥M layouts list and the
/// welcome, each a <see cref="ModalOverlay"/> added as the last child of the window's root panel — over the cells, the
/// native panels, F1 and the chrome. The client calls in on its own thread, so everything here is posted to the
/// window's. A presenter never attached (<c>--render-png</c>, the parity harness, a test) is not <see cref="Available"/>,
/// and the client opens its cell modals instead.
/// <para>
/// <b>The quit prompt is always in front.</b> One other modal may be open under it. Whenever anything is added to the
/// root after the prompt (⌃P, F1, another modal), the prompt is moved back to the end; and showing it brings the main
/// window in front of the settings window, so a close asked for from anywhere is never answered blind.
/// </para>
/// <para>
/// <b>The keyboard goes back where it was.</b> The element focused when a modal opened takes it back when the modal
/// closes, if it is still on screen (the command line, by the client's focus pin, or F1's editor); otherwise the
/// main window's refocus decides. With the prompt closed over another modal, that modal takes it.
/// </para>
/// </summary>
internal sealed class ModalPresenter : INativeModals
{
    private volatile bool _attached;
    private Window? _owner;
    private Panel? _root;
    private Action? _refocus;
    private Action<ConsoleKeyInfo>? _forward;
    private ModalOverlay? _modal;
    private ModalOverlay? _quit;
    private bool _reordering;

    public bool Available => _attached;

    /// <summary>The open modal under the quit prompt (search, history, layouts, welcome), for a test; window's thread.</summary>
    public ModalOverlay? Modal => _modal;

    /// <summary>The open quit prompt, for a test; window's thread.</summary>
    public ModalOverlay? Quit => _quit;

    /// <summary>
    /// Where the modals are drawn (<paramref name="root"/>, the window's root panel), the window brought forward for
    /// the quit prompt, what takes the keyboard when nothing else can, and how a chord the client answers from any
    /// modal (⌃Q) is handed to it. On the window's thread.
    /// </summary>
    public void Attach(Window? owner, Panel root, Action refocus, Action<ConsoleKeyInfo> forward)
    {
        _owner = owner;
        _root = root;
        _refocus = refocus;
        _forward = forward;
        root.Children.CollectionChanged += KeepQuitInFront;
        _attached = true;
    }

    /// <summary>The window's text sizes, read when a surface opens (MainWindow sets it; designed sizes until then).</summary>
    public Func<NativeScales> Scales { get; set; } = () => NativeScales.One;

    public void Show(ModalSession session) => Dispatcher.UIThread.Post(() => ShowNow(session));

    public void Close(ModalSession session) => Dispatcher.UIThread.Post(() =>
    {
        if (_quit is { } quit && ReferenceEquals(quit.Session, session))
        {
            Remove(quit);
        }
        else if (_modal is { } modal && ReferenceEquals(modal.Session, session))
        {
            Remove(modal);
        }
    });

    /// <summary>Shows <paramref name="session"/> now; on the window's thread.</summary>
    internal ModalOverlay? ShowNow(ModalSession session)
    {
        if (_root is not { } root || session.IsEnded)
        {
            session.Dismiss();
            return null;
        }

        var previousFocus = TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement();
        var overlay = new ModalOverlay(session, Remove, Forward, Scales());
        if (session.AlwaysInFront)
        {
            if (_quit is { } old)
            {
                Remove(old);
            }

            overlay.ReturnFocusTo = _modal is null ? previousFocus : null;
            _quit = overlay;
            root.Children.Add(overlay);
            _owner?.Activate();
        }
        else
        {
            var returnTo = previousFocus;
            if (_modal is { } old)
            {
                returnTo = old.ReturnFocusTo;
                Remove(old, refocus: false);
                old.Session.Dismiss();
            }

            overlay.ReturnFocusTo = returnTo;
            _modal = overlay;

            // Under the quit prompt when one is up: the prompt stays the thing being answered.
            var at = _quit is not null ? root.Children.IndexOf(_quit) : -1;
            root.Children.Insert(at < 0 ? root.Children.Count : at, overlay);
        }

        if (_quit is null || ReferenceEquals(_quit, overlay))
        {
            overlay.FocusFirst();

            // A view whose first field is not laid out yet refuses the focus, which then stays on the cells and every key
            // goes to the command line under the modal; once it is laid out, it takes the keyboard if nothing in it has.
            Dispatcher.UIThread.Post(() =>
            {
                if ((ReferenceEquals(_quit, overlay) || (_quit is null && ReferenceEquals(_modal, overlay))) && !overlay.IsKeyboardFocusWithin)
                {
                    overlay.FocusFirst();
                }
            }, DispatcherPriority.Loaded);
        }

        return overlay;
    }

    private void Forward(ConsoleKeyInfo key) => _forward?.Invoke(key);

    private void Remove(ModalOverlay overlay) => Remove(overlay, refocus: true);

    private void Remove(ModalOverlay overlay, bool refocus)
    {
        if (ReferenceEquals(_quit, overlay))
        {
            _quit = null;
        }
        else if (ReferenceEquals(_modal, overlay))
        {
            _modal = null;
        }
        else
        {
            return;
        }

        overlay.Detach();
        _root?.Children.Remove(overlay);
        if (!refocus)
        {
            return;
        }

        if (_quit is { } quit)
        {
            quit.FocusFirst();
        }
        else if (_modal is { } modal)
        {
            modal.FocusFirst();
        }
        else if (overlay.ReturnFocusTo is Visual { IsEffectivelyVisible: true } back
                 && TopLevel.GetTopLevel(back) is not null
                 && back is IInputElement element
                 && element.Focus())
        {
            // The command line or F1's editor has the keyboard again.
        }
        else
        {
            _refocus?.Invoke();
        }
    }

    /// <summary>Anything added to the root after the quit prompt goes under it.</summary>
    private void KeepQuitInFront(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_reordering || e.Action != NotifyCollectionChangedAction.Add || _quit is not { } quit || _root is not { } root)
        {
            return;
        }

        if (root.Children.Count == 0 || ReferenceEquals(root.Children[^1], quit))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_quit, quit) || ReferenceEquals(root.Children[^1], quit))
            {
                return;
            }

            _reordering = true;
            try
            {
                root.Children.Remove(quit);
                root.Children.Add(quit);
            }
            finally
            {
                _reordering = false;
            }

            quit.FocusFirst();
        });
    }
}
