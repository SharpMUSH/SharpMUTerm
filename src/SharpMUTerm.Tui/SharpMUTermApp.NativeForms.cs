using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Protocols;

namespace SharpMUTerm.Tui;

/// <summary>
/// The last three forms the window still drew in cells: the ⌃B which-key panel, the client messages and the game data
/// inspector. Each now asks the host first (<see cref="ClientHost.NativeModals"/>), and the cell form stays the
/// terminal's. The model is the one the cell form draws (<see cref="PrefixPanel.Model(PrefixFacts)"/>,
/// <see cref="MessageLogModel"/>, <see cref="GameDataModel"/>), and every gesture ends in the method the cell keys end in.
/// <para>
/// <b>The window's ⌃B panel opens at once.</b> The terminal waits <see cref="PrefixPanelDelay"/> before its panel, so
/// an expert's second key lands first and the panel is never drawn; that wait read as a slow menu in the window, where
/// ⌃B is a chord a pointer user presses to see the keys. A key that arrives before the card does still goes to the
/// prefix consumer through the cells, which closes the card.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    // ── ⌃B ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Opens the ⌃B panel in the host's controls; false when the host has none.</summary>
    private bool TryOpenNativePrefixPanel()
    {
        if (NativeModalsHost is not { } native)
        {
            return false;
        }

        if (NativeModalOf(SurfaceCatalog.PrefixPanel) is not null)
        {
            return true;
        }

        PrefixPanelSession? session = null;
        session = new PrefixPanelSession(
            PrefixPanel.Model(PrefixFactsNow()),
            _workspace.Layout.Panes.Count,
            ThemeIsDark(),
            key => OnUiThread(() => ConsumePrefixKey(key)),
            () => OnUiThread(() =>
            {
                NativeModalClosed(session!);

                // Esc, a press beside the card, or a key: every way off the panel is a way off the prefix.
                DisarmPrefix();
            }))
        {
            Theme = _theme.Clone(),
        };
        ShowNativeModal(native, session);
        return true;
    }

    /// <summary>Takes the native ⌃B panel down, running nothing: what disarming does to it.</summary>
    private void CloseNativePrefixPanel()
    {
        if (NativeModalOf(SurfaceCatalog.PrefixPanel) is { } open)
        {
            CloseNativeModal(open);
        }
    }

    // ── client messages ─────────────────────────────────────────────────────────────────────────

    /// <summary>⌃P ▸ Show client messages, in either host, or closes it when it is open.</summary>
    private void ToggleMessages()
    {
        if (NativeModalOf(SurfaceCatalog.Messages) is { } open)
        {
            CloseNativeModal(open);
            return;
        }

        if (_messageLog.IsOpen || NativeModalsHost is not { } native)
        {
            _messageLog.Toggle();
            return;
        }

        MessageLogSession? session = null;
        session = new MessageLogSession(
            MessageLogOverlay.ModelOf(_diagnostics),
            ThemeIsDark(),
            delta => OnUiThread(() => StepMessageLevel(delta)),
            () => OnUiThread(() => NativeModalClosed(session!)))
        {
            Theme = _theme.Clone(),
        };
        ShowNativeModal(native, session);
    }

    /// <summary>
    /// One level louder (−1) or quieter (+1) for what the client keeps: the cells' <c>+</c>/<c>-</c> and the window's
    /// buttons. The surface open is redrawn from the new model.
    /// </summary>
    private void StepMessageLevel(int delta)
    {
        if (_messageLog.IsOpen)
        {
            _messageLog.StepLevel(delta);
            return;
        }

        MessageLogOverlay.Step(_diagnostics, delta);
        if (NativeModalOf(SurfaceCatalog.Messages) is MessageLogSession session)
        {
            session.Publish(MessageLogOverlay.ModelOf(_diagnostics));
        }
    }

    // ── game data ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Opens the game data inspector in the host's controls; false when the host has none.</summary>
    private bool TryOpenNativeGameData(GameDataStore data, string owner)
    {
        if (NativeModalsHost is not { } native)
        {
            return false;
        }

        GameDataSession? session = null;
        session = new GameDataSession(
            GameDataModel.From(data.Entries, owner, _time.GetUtcNow()),
            ThemeIsDark(),
            () => OnUiThread(() => NativeModalClosed(session!)))
        {
            Theme = _theme.Clone(),
        };
        ShowNativeModal(native, session);
        return true;
    }

    /// <summary>The native messages' session while one is open, for the tests.</summary>
    internal MessageLogSession? NativeMessages => _nativeModal as MessageLogSession;

    /// <summary>The native ⌃B panel's session while one is open, for the tests.</summary>
    internal PrefixPanelSession? NativePrefixPanel => _nativeModal as PrefixPanelSession;

    /// <summary>The quietest level the client's messages capture, for the tests.</summary>
    internal string MessageLogLevel => MessageLogOverlay.ModelOf(_diagnostics).Level;

    /// <summary>Feeds one key to the open cell message log, for the tests.</summary>
    internal void SimulateMessageLogKey(ConsoleKeyInfo key) => _messageLog.SimulateKey(key);
}
