using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Logging;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// Scene logs (⌃P ▸ <em>Start scene log</em> / <em>Stop scene log</em>): the focused window's lines from now until it is
/// stopped, written to a file of their own under the log root through the session log's sinks (<see cref="SceneLog"/>),
/// with a header naming the scene and who was present — the window's room roster (who-here) and its character. F7 ▸
/// Scene log says HTML or plain text, and poses or every line.
/// <para>
/// <b>The log root is the gate</b>, as it is for the session log: an app with none (a test, a snapshot) refuses out loud
/// and writes nothing. Lines are fed where the restore log is (<see cref="OnLine"/>, <see cref="OnSpawnLine"/>), so a
/// scene in a channel window records that window, and the client's own chrome rows are never offered.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The scenes being recorded, by window.</summary>
    private readonly Dictionary<string, SceneLog> _sceneLogs = new(StringComparer.Ordinal);

    /// <summary>The scene log recording a window, for the tests.</summary>
    internal SceneLog? SceneLogOf(string windowId)
    {
        lock (_sceneLogs)
        {
            return _sceneLogs.GetValueOrDefault(windowId);
        }
    }

    /// <summary>Starts recording the focused window as a scene, titled after the window.</summary>
    internal void StartSceneLog()
    {
        var windowId = ActiveWindowId();
        if (_logRoot is null)
        {
            RefuseCommand("no scene log — this client owns no log directory");
            return;
        }

        if (SceneLogOf(windowId) is not null)
        {
            RefuseCommand($"{Snippet(WindowTitle(windowId))} is already being recorded");
            return;
        }

        var session = WindowSession(windowId);
        var key = session?.SessionKey ?? _workspace.FindWindow(windowId)?.SessionKey;
        var present = new List<string>();
        if (key is not null && PanelOccupants(key) is { Known: true } roster)
        {
            present.AddRange(roster.Snapshot.Where(o => o.IsPlayer).Select(o => o.Name));
        }

        var own = session?.Character?.Name ?? (key is not null && IsDemoKey(key) ? "Corvid" : null);
        if (own is not null && !present.Contains(own, StringComparer.OrdinalIgnoreCase))
        {
            present.Insert(0, own);
        }

        var title = WindowTitle(windowId);
        SceneLog log;
        try
        {
            log = SceneLog.Start(
                Path.Combine(_logRoot, "scenes"),
                windowId,
                title,
                _config.SceneLog.Html,
                _config.SceneLog.Content,
                present,
                _time.GetLocalNow());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RefuseCommand($"the scene log could not be opened: {ex.Message}");
            return;
        }

        lock (_sceneLogs)
        {
            _sceneLogs[windowId] = log;
        }

        Notice(
            $"Recording {Snippet(title)} as a scene ({(_config.SceneLog.Content == SceneLogContent.Poses ? "poses" : "every line")}) → {log.Path}",
            MessageSeverity.Info,
            "⌃P");
    }

    /// <summary>Stops the focused window's scene log, writing its ending and closing the file.</summary>
    internal void StopSceneLog()
    {
        var windowId = ActiveWindowId();
        SceneLog? log;
        lock (_sceneLogs)
        {
            _sceneLogs.Remove(windowId, out log);
        }

        if (log is null)
        {
            RefuseCommand($"{Snippet(WindowTitle(windowId))} is not being recorded");
            return;
        }

        log.Stop(_time.GetLocalNow());
        Notice($"Scene saved · {log.Lines} line{(log.Lines == 1 ? string.Empty : "s")} · {log.Path}", MessageSeverity.Info, "⌃P");
    }

    /// <summary>A line a window showed: the scene recording that window, if any, decides whether to keep it.</summary>
    private void RecordForScene(string windowId, StyledLine line)
    {
        SceneLog? log;
        lock (_sceneLogs)
        {
            if (_sceneLogs.Count == 0 || !_sceneLogs.TryGetValue(windowId, out log))
            {
                return;
            }
        }

        try
        {
            log.Offer(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            lock (_sceneLogs)
            {
                _sceneLogs.Remove(windowId);
            }

            RefuseCommand($"the scene log stopped: {ex.Message}");
        }
    }

    /// <summary>Closes every scene being recorded, on the way out.</summary>
    private void CloseSceneLogs()
    {
        SceneLog[] logs;
        lock (_sceneLogs)
        {
            logs = [.. _sceneLogs.Values];
            _sceneLogs.Clear();
        }

        foreach (var log in logs)
        {
            log.Stop(_time.GetLocalNow());
        }
    }

    // ── demo scenes ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The snapshot scenes for channel capture, highlight names and the scene log, over the demo. The demo has no live
    /// session, so a channel message goes through <see cref="ChannelCapture.Read"/> and the routing a captured line
    /// takes (<see cref="Workspace.RouteLine"/>, <see cref="AppendSessionLine"/>), and a name through
    /// <see cref="NameHighlights.Apply"/>: the functions the session runs. Returns a frame for a view drawn here alone,
    /// else null to go on drawing the workspace.
    /// </summary>
    private string? RenderScriptingWaveScene(string? view)
    {
        if (view is null || _config.Worlds.Count == 0)
        {
            return null;
        }

        var world = _config.Worlds[0];
        var names = new List<NameHighlight> { new("Ashlyn", "gold"), new("Merrow", "red") };
        if (string.Equals(view, "channels-f5", StringComparison.OrdinalIgnoreCase))
        {
            world.CaptureChannels = ChannelCaptureMode.Copy;
            world.HighlightNames = names;
            _settings.OpenForSnapshot(ConsoleKey.F5, WorldsScreen());

            // Edit the world and ⇥ through its fields to the names row, so the frame shows the two new rows rather
            // than "19 more" — the keys a reader presses.
            _settings.SimulateKey(Stroke('\r', ConsoleKey.Enter));
            for (var i = 0; i < WorldsScreenRenderer.WorldFieldOrder.ToList().IndexOf("highlight-names"); i++)
            {
                _settings.SimulateKey(Stroke('\t', ConsoleKey.Tab));
            }

            SyncInputWidth();
            return RenderFrame();
        }

        if (string.Equals(view, "highlight-names", StringComparison.OrdinalIgnoreCase))
        {
            world.HighlightNames = names;
            var matcher = NameHighlights.Compile(names);
            foreach (var text in new[]
            {
                "Ashlyn sips her tea and glances across the plaza at Merrow.",
                "Merrow says, \"Ashlyn's late again.\" Annabel laughs.",
                "Brannoc nods to Ashlyn, Merrow and you.",
            })
            {
                AppendSessionLine(MainWindowId, NameHighlights.Apply(StyledLine.FromText(text, TextStyle.Default), matcher), StampNow());
            }

            SettleScroll();
            return null;
        }

        if (string.Equals(view, "channel-capture", StringComparison.OrdinalIgnoreCase))
        {
            world.CaptureChannels = ChannelCaptureMode.Copy;
            string? oocId = null;
            foreach (var json in DemoChannelMessages)
            {
                if (ChannelCapture.Read(json) is not { } message)
                {
                    continue;
                }

                foreach (var line in message.Lines)
                {
                    var window = _workspace.RouteLine(message.Window, DemoScene.ActiveSessionKey);
                    window.OwnerLabel ??= "Corvid";
                    PaneContentFor(window.Id, window.Title);
                    AppendSessionLine(window.Id, line, StampNow());
                    AppendSessionLine(MainWindowId, line, StampNow());
                    if (message.Window == "ooc")
                    {
                        oocId = window.Id;
                    }
                }
            }

            if (oocId is not null)
            {
                Activate(MainWindowId);
                PaneCommands.Apply(_workspace.Layout, PaneCommand.SplitRight);
                RebuildPaneArea();
                Activate(oocId);
            }

            SettleScroll();
            return null;
        }

        if (string.Equals(view, "scene-log", StringComparison.OrdinalIgnoreCase))
        {
            OpenMenu("scene");
        }

        return null;
    }

    /// <summary>Four <c>Comm.Channel.Text</c> payloads as IRE sends them, for the channel-capture scene.</summary>
    private static readonly string[] DemoChannelMessages =
    [
        "{\"channel\":\"ooc\",\"talker\":\"Ashlyn\",\"text\":\"\\u001b[36m(OOC) Ashlyn: anyone up for a scene at the docks?\\u001b[0m\"}",
        "{\"channel\":\"ooc\",\"talker\":\"Merrow\",\"text\":\"\\u001b[36m(OOC) Merrow: in ten, grabbing tea\\u001b[0m\"}",
        "{\"channel\":\"trade\",\"talker\":\"Brannoc\",\"text\":\"\\u001b[33m[Trade] Brannoc: selling lanterns, 4 silver\\u001b[0m\"}",
        "{\"channel\":\"ooc\",\"talker\":\"Ashlyn\",\"text\":\"\\u001b[36m(OOC) Ashlyn: \\u001b[1msee you there\\u001b[0m\"}",
    ];
}
