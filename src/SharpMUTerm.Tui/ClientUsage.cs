using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Telnet.Mssp;

namespace SharpMUTerm.Tui;

/// <summary>One row of a help page's option table.</summary>
internal sealed record UsageOption(string Flag, string Help, string? More = null);

/// <summary>A launcher's own part of the help page: its name, its command, and the option sections only it has.</summary>
internal sealed record UsageLauncher(string Title, string Command, IReadOnlyList<(string Heading, IReadOnlyList<UsageOption> Options)> Sections);

/// <summary>
/// The options every launcher takes because the client takes them — parsed by <see cref="Program"/>'s live path,
/// whichever host it runs in. The desktop window passes everything it does not recognise to the client, so a
/// row added here is an option both hosts accept and both help pages list.
/// </summary>
internal static class ClientOptions
{
    public static IReadOnlyList<UsageOption> Connection { get; } =
    [
        new("host", "Server hostname or IP (IPv4/IPv6)."),
        new("port", "Server port (default 4000)."),
        new("--tls", "Connect over TLS."),
        new("--insecure", "Accept invalid TLS certificates."),
        new("--name <name>", "Display name for the world."),
    ];

    /// <summary>The options that take a value, so their value is never mistaken for the host.</summary>
    public static IReadOnlySet<string> TakingValues { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "--name", "--size", "--view", "--out", "--theme", "--transparency", "--font", "--font-size", "--render-png",
    };

    /// <summary>The arguments that are not an option or an option's value: the host, then the port.</summary>
    public static string[] Positional(string[] args)
    {
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith('-'))
            {
                if (TakingValues.Contains(args[i]))
                {
                    i++;
                }

                continue;
            }

            positional.Add(args[i]);
        }

        return [.. positional];
    }
}

/// <summary>
/// The help page, for every launcher. The parts every host shares — the client's options, the files it keeps,
/// the in-app keys — are written once, and the keys that differ between hosts are read from the host's
/// <see cref="HostKeys"/>; a launcher supplies only its title, its command and its own options.
/// </summary>
internal static class ClientUsage
{
    public static UsageLauncher Terminal { get; } = new(
        "SharpMUTerm — a cross-platform TUI MU* client.",
        "sharpmuterm",
        [
            ("Snapshot (render one frame headlessly and exit):",
            [
                new("--snapshot", "Render one frame (ANSI) headlessly and exit."),
                new("--size <WxH>", "Snapshot size in cells (default 160x48)."),
                new("--view <name>", "Snapshot an overlay (e.g. 'settings') over the workspace.",
                    "'<name>-edit' opens that settings screen mid field edit."),
                new("--demo-config", "Snapshot the built-in demo worlds instead of your own."),
                new("--out <file>", "Write the snapshot to a file instead of stdout."),
            ]),
        ]);

    private static void Options(TextWriter usage, IEnumerable<UsageOption> options)
    {
        foreach (var option in options)
        {
            usage.WriteLine($"  {option.Flag,-21}{option.Help}");
            if (option.More is { } more)
            {
                usage.WriteLine($"  {"",-21}{more}");
            }
        }
    }

    public static void Write(TextWriter usage, UsageLauncher launcher, HostKeys keys)
    {
        usage.WriteLine(launcher.Title);
        usage.WriteLine();
        usage.WriteLine($"Usage: {launcher.Command} [host] [port] [options]");
        usage.WriteLine();
        Options(usage, ClientOptions.Connection);
        foreach (var (heading, options) in launcher.Sections)
        {
            usage.WriteLine();
            usage.WriteLine(heading);
            Options(usage, options);
        }

        usage.WriteLine();
        Options(usage, [new UsageOption("-h, --help", "Show this help.")]);
        usage.WriteLine();
        usage.WriteLine($"Config: {ConfigurationStore.DefaultPath}");

        // Named because it is the file a user has to know about to look after it — and because "where did my
        // password go" should be answerable without reading the source. Config is safe to share; this is not.
        usage.WriteLine($"Secrets: {SecretsStore.PathFor(ConfigurationStore.DefaultPath)}"
            + " — character passwords, plain text, owner-only. Not the file to paste.");

        // Named for the same reason: it is a file this client creates in the user's own directory, and
        // "what is this and can I delete it" should be answerable from the help page. It can: it is a
        // cache of what servers published, and deleting it costs only the next connection's report.
        usage.WriteLine($"Servers: {MsspCache.PathFor(ConfigurationStore.DefaultPath)}"
            + " — each server's last MSSP report (F5 ▸ i). A cache; safe to delete.");
        // "why does it connect to *that*?" is the question this setting answers, so the answer belongs
        // on the page a user reaches for when they ask it. Both halves are stated: what connects with no
        // host, and that a host overrides it.
        usage.WriteLine("With no host, the characters marked 'at start' on F5 connect — none by default,");
        usage.WriteLine("and the client opens with no connection. A host given here connects instead of them.");
        usage.WriteLine("'at start' only opens the connection. What is typed once one is open follows from the");
        usage.WriteLine("character's saved password and connect line — F5's 'login' row says which.");
        usage.WriteLine();
        usage.WriteLine("In-app: Up/Down history · Ctrl+N next tab · Ctrl+W close · Ctrl+P palette · Ctrl+Q quit.");
        usage.WriteLine("Selection: drag across a pane's output to select it, Ctrl+C to copy.");
        // The composer earns a line of its own because what it *sends* is not guessable from the window:
        // the buffer is one command and its line breaks are written %r, which is what a MUSH board or
        // mail body wants. Naming the send chord matters for the same reason — Ctrl+Enter is what a
        // reader will try, and no Unix terminal reports it distinctly.
        usage.WriteLine("Write:  F1 opens a full-screen editor for a post. The whole buffer is one command and");
        usage.WriteLine("        its line breaks are sent as %r; Ctrl+S sends, Alt+L switches between sending the");
        usage.WriteLine("        text as typed and escaping it, Esc closes and keeps the draft for that character.");
        usage.WriteLine("Scroll: PgUp/PgDn a page · Shift+Up/Down a line · Ctrl+Home top · Ctrl+End back to live output.");
        usage.WriteLine("Focus:  Ctrl+Left/Right/Up/Down move between panes (Ctrl+Down at the bottom reaches the second");
        usage.WriteLine("        command line); Ctrl+O cycles them; Tab switches command lines. The pane you are on and");
        usage.WriteLine("        the line Enter sends from are both drawn lit, and the focused pane's tab is marked.");

        // Alt, not Ctrl, and the page says why: Ctrl+digit is what a reader will try first (it is what was
        // asked for) and it cannot work — no digit has a control byte of its own, so a terminal sends the
        // bare digit, or one already spelt Escape or Backspace. Naming the working chord and the reason
        // the obvious one is absent is the same honesty this page owes everywhere else.
        //
        // It says *window* and not pane. The chord counted panes until a capture window sharing a pane
        // turned out to be the thing people wanted to reach; the numbered pane jump is ⌃B N now, and both
        // are named here so a reader is not left thinking one replaced the other silently.
        // The page names chords in ASCII throughout — it is printed before the TUI starts, into whatever
        // is on the other end of stdout, which may be a pipe or a terminal with no ⌥ glyph. But where a
        // sentence claims to reproduce what is *on the screen*, it has to be verbatim: these quotes are
        // the sidebar's own cells, and a reader who searches the sidebar for "Alt+2" finds nothing.
        usage.WriteLine("Windows: Alt+1..Alt+9 go straight to a numbered window and bring it forward — a character's");
        usage.WriteLine("        own window, a capture window, the web view. The numbers are the ones the sidebar");
        usage.WriteLine("        prints beside each window ('⌥2', '⌥3'...), in the order the windows were opened.");
        usage.WriteLine("        It says so when there is no window with that number.");
        usage.WriteLine("        Ctrl+digit is not offered: no terminal sends a distinct Ctrl+digit — 3 and 8 arrive");
        usage.WriteLine("        as Escape and Backspace, and 1, 9 and 0 as the bare digit.");
        usage.WriteLine("Panes:  Ctrl+B then 1..9 goes to a numbered pane instead — the pane itself, whatever tab it");
        usage.WriteLine("        is showing. Panes are numbered in the order they were created, which is the numbering");
        usage.WriteLine("        Ctrl+O counts in and the one the move overlay (Ctrl+B m) badges each pane with.");

        // Alt+Shift+arrow is a chord this host does deliver — the parser reads both modifier bits out of
        // CSI 1;4 <final> — which is why it may be named here at all; see TerminalKeyArrivalTests. It is
        // deliberately not Ctrl+Shift+arrow, which this page used to name: kitty_mod is ctrl+shift, and
        // kitty consumes ctrl+shift+left/right for its own tabs, so half of that chord never arrived.
        usage.WriteLine("Size:   Alt+Shift+Up/Down/Left/Right make the focused pane taller, shorter, narrower or");
        usage.WriteLine("        wider by one character cell — the arrow says what happens to the pane, wherever it");
        usage.WriteLine("        sits (it says so when there is no split that way, or the pane paying is at its");
        usage.WriteLine("        smallest).");

        // Alt+Enter only. Shift+Enter and Ctrl+Enter are deliberately not listed: no Unix
        // terminal reports them distinctly through SharpConsoleUI's input parser (both arrive as a bare
        // Enter), and a help page naming a key that cannot fire is the defect this file is careful about.
        usage.WriteLine($"Typing: {keys.Newline.Text} inserts a newline · Home/Ctrl+E line ends · Ctrl+K/U kill ·");
        usage.WriteLine("        Alt+Left/Right by word · Ctrl+Z/Y undo and redo · Ctrl+R searches history. Up/Down");
        usage.WriteLine("        and Alt+Up/Down recall what was sent to the focused character; each character keeps");
        usage.WriteLine("        its own history.");
        usage.WriteLine("Copy:   Ctrl+C copies the selection — drag across output or the command line, or select");
        usage.WriteLine("        in the command line with Shift+Left/Right, Shift+Home/End or Ctrl+A (all). Ctrl+X");
        usage.WriteLine("        cuts the command line's selection.");
        // The connection pair, described by what the key does rather than by what it asks — it asks
        // nothing. Both act at once, on the character whose pane is focused, and "drops and redials" is
        // spelt out because a reconnect on a live connection is a disconnect with a dial after it and the
        // reader has to know that before pressing it.
        usage.WriteLine("World:  Alt+R reconnects the focused character (drops the connection and redials it at once);");
        usage.WriteLine("        Alt+D disconnects it at once. Neither asks. With nothing connected, each says so.");
        usage.WriteLine("        Alt+J and Alt+K move to the next and previous character you have open — the two the");
        usage.WriteLine("        sidebar marks '⌥J' and '⌥K'. They never open one that is not; the sidebar and");
        usage.WriteLine("        Ctrl+P do that.");
        usage.WriteLine("Panes:  Ctrl+B then | - z o 1-9 x b m i < > splits, zooms, goes and moves; Esc or Ctrl+B");
        usage.WriteLine("        cancels, and pausing after Ctrl+B pops a panel naming each key. Or drag a tab");
        usage.WriteLine("        strip onto another pane — middle drops it as a tab, an edge splits there.");

        // What this host keeps for itself, from the same list it acts on, so the page cannot offer one of them.
        foreach (var group in keys.Reserved.GroupBy(r => r.Action))
        {
            usage.WriteLine($"Host:   {string.Join(", ", group.Select(r => r.Chord.Text))} {group.Key}.");
        }
    }
}
