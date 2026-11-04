using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Tui;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window spells a key one way on every native surface — in words (<c>Alt+2</c>, <c>Ctrl+W</c>), through
/// <see cref="NativeKeys.Words"/> — while the terminal keeps its glyphs. A menu saying <c>⌥2</c> beside a sidebar saying
/// <c>Alt+2</c> was the report. Each native surface is opened over the demo in a real window and every word it shows —
/// text, key chips, buttons, menu entries, tooltips, a toast — is scanned for a modifier glyph; and the window's own
/// source holds none in a string.
/// </summary>
[NotInParallel]
public class NativeKeySpellingTests
{
    private sealed record Rig(Window Window, Panel Root, TerminalView View, DesktopLayer Layer, SharpMUTermApp App, AvaloniaConsoleDriver Driver);

    private static Task<T> Run<T>(Func<Rig, T> body) =>
        TerminalViewTests.Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(120, 34);
            var view = new TerminalView(driver, SKColors.White, new SKColor(0x1e, 0x1e, 0x24), 14, null);
            var root = new Panel();
            root.Children.Add(view);
            var layer = new DesktopLayer(view, HostChrome.HeaderAndRail);
            root.Children.Add(layer);
            driver.Composer.Attach(root, layer, view, () => view.Focus());
            driver.Palette.Attach(root, () => view.Focus());
            var window = new Window { Content = root, Width = 1200, Height = 760 };
            driver.Modals.Attach(window, root, () => view.Focus(), driver.RaiseKey);
            window.Show();
            view.Focus();
            var app = new SharpMUTermApp(DemoScene.Build(), WindowHost.Create(driver, clipboard: null) with
            {
                Audio = null,
                Presence = null,
                Announcer = null,
                NativeSettings = null,
                RunsInline = true,
            });
            driver.KeyPressed += (_, key) => app.SimulateKey(key);
            try
            {
                app.RenderSnapshot(null);
                DesktopLayerTests.Drain();
                return body(new Rig(window, root, view, layer, app, driver));
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

    private static void Key(Rig rig, ConsoleKeyInfo key)
    {
        rig.App.SimulateKey(key);
        DesktopLayerTests.Drain();
    }

    private static ConsoleKeyInfo Ctrl(char letter, ConsoleKey key) => new((char)(letter - 'a' + 1), key, false, false, true);

    /// <summary>Every word a native view shows: text (and its runs), string content, headers and tooltips.</summary>
    internal static IEnumerable<string> Words(ILogical root)
    {
        foreach (var node in root.GetLogicalDescendants().Prepend(root))
        {
            if (node is TextBlock block)
            {
                if (block.Text is { Length: > 0 } text)
                {
                    yield return text;
                }

                if (block.Inlines is { } inlines)
                {
                    yield return string.Concat(inlines.OfType<Run>().Select(r => r.Text));
                }
            }

            if (node is ContentControl { Content: string content })
            {
                yield return content;
            }

            if (node is HeaderedItemsControl { Header: string header })
            {
                yield return header;
            }

            if (node is Control control && ToolTip.GetTip(control) is string tip)
            {
                yield return tip;
            }
        }
    }

    private static List<string> Glyphed(string surface, IEnumerable<string> words) =>
        words.Where(NativeKeys.HasGlyph).Select(w => $"{surface}: {w}").ToList();

    [Test]
    public async Task NoNativeSurfaceSpellsAKeyInGlyphs()
    {
        var (found, scanned) = await Run(rig =>
        {
            var found = new List<string>();
            var scanned = new List<string>();

            // The right-click menu over the output: the client's entries with their keys (⌥2, ⌃W in the cells).
            var menu = NativeMenu.Build(rig.App.PointerMenu(new System.Drawing.Point(40, 8)), PlacementMode.Pointer, rig.View);
            var menuWords = Words(menu).ToList();
            found.AddRange(Glyphed("menu", menuWords));
            scanned.Add($"menu:{menuWords.Count}");
            if (!menuWords.Any(w => w.Contains("Ctrl+", StringComparison.Ordinal) || w.Contains("Alt+", StringComparison.Ordinal)))
            {
                found.Add("menu: no key in words either — " + string.Join(" | ", menuWords));
            }

            // The header's Layouts menu.
            var layouts = NativeMenu.Build(rig.App.LayoutsMenu(), _ => { }, PlacementMode.Bottom, rig.View);
            found.AddRange(Glyphed("layouts menu", Words(layouts)));

            // A toast whose text, key chip and action all name keys.
            rig.App.SimulateNotice("⌃P ▸ Layouts, or ⌥M", MessageSeverity.Warning, "⌥M", new NoticeAction("layout:layouts", "Open ⌥M"));
            DesktopLayerTests.Drain();
            rig.Driver.Flush();
            DesktopLayerTests.Drain();
            var toast = rig.Layer.Toasts.DrawnText();
            found.AddRange(Glyphed("toast", toast));
            scanned.Add($"toast:{toast.Count}");
            if (!toast.Contains("Alt+M"))
            {
                found.Add("toast: the key chip is not Alt+M — " + string.Join(" | ", toast));
            }

            // The native header and sidebar: their tooltips, and the armed ⌃B keymap.
            found.AddRange(Glyphed("header", rig.Layer.Header.Hits.Select(h => h.Tip)));
            found.AddRange(Glyphed("sidebar", rig.Layer.Sidebar.Hits.Select(h => h.Tip)));
            Key(rig, Ctrl('b', ConsoleKey.B));
            found.AddRange(Glyphed("prefix", rig.Layer.Header.Hits.Select(h => h.Tip)));
            Key(rig, new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));

            // ⌃P: every row's title, detail and key chips.
            Key(rig, Ctrl('p', ConsoleKey.P));
            if (rig.Driver.Palette.Overlay is { } palette)
            {
                var words = Words(palette).ToList();
                found.AddRange(Glyphed("palette", words));
                scanned.Add($"palette:{words.Count}");
                Key(rig, Ctrl('p', ConsoleKey.P));
            }
            else
            {
                found.Add("palette: did not open natively");
            }

            // F1: the composer's key row.
            Key(rig, new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false));
            if (rig.Driver.Composer.Overlay is { } composer)
            {
                var words = Words(composer).ToList();
                found.AddRange(Glyphed("composer", words));
                scanned.Add($"composer:{words.Count}");
                Key(rig, new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false));
            }
            else
            {
                found.Add("composer: did not open natively");
            }

            // The modals: ⌃F, ⌃R, ⌥M, and the quit prompt.
            foreach (var (name, key) in new[]
            {
                ("search", Ctrl('f', ConsoleKey.F)),
                ("history", Ctrl('r', ConsoleKey.R)),
                ("layouts", new ConsoleKeyInfo('m', ConsoleKey.M, false, true, false)),
                ("quit", Ctrl('q', ConsoleKey.Q)),
            })
            {
                Key(rig, key);
                if ((rig.Driver.Modals.Quit ?? rig.Driver.Modals.Modal) is { } modal)
                {
                    var words = Words(modal).ToList();
                    found.AddRange(Glyphed(name, words));
                    scanned.Add($"{name}:{words.Count}");
                    Key(rig, new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
                    if ((rig.Driver.Modals.Quit ?? rig.Driver.Modals.Modal) is { } still)
                    {
                        still.Session.Dismiss();
                        rig.Driver.Modals.Close(still.Session);
                        DesktopLayerTests.Drain();
                    }
                }
                else
                {
                    found.Add($"{name}: did not open natively");
                }
            }

            return (found, scanned);
        });

        await Assert.That(found).IsEmpty().Because(string.Join("\n", found));
        await Assert.That(scanned.Count).IsGreaterThanOrEqualTo(7).Because("every surface was opened and read: " + string.Join(", ", scanned));
    }

    /// <summary>The window's own strings — a footer, a tooltip, an empty state — name keys in words too.</summary>
    [Test]
    public async Task TheWindowsOwnStringsSpellKeysInWords()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "SharpMUTerm.Avalonia");
        var literal = new Regex("\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                // The list of glyphs names them, and the host contract carries the chords' glyph spellings for the
                // client's cells (the palette's rows reach the window through NativeKeys like everything else).
                if (Path.GetFileName(file) is "NativeKeys.cs" or "WindowHost.cs")
                {
                    continue;
                }

                foreach (Match match in literal.Matches(line))
                {
                    if (NativeKeys.HasGlyph(match.Value))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {match.Value}");
                    }
                }
            }
        }

        await Assert.That(offenders).IsEmpty().Because(string.Join("\n", offenders));
    }

    [Test]
    public async Task WordsSpellsEveryModifierAndLeavesTheRestAlone()
    {
        await Assert.That(NativeKeys.Words("⌥2 · ⌃W · ⌥⇧→ · ⌃B b")).IsEqualTo("Alt+2 · Ctrl+W · Alt+Shift+→ · Ctrl+B b");
        await Assert.That(NativeKeys.Words("F4 edits keys")).IsEqualTo("F4 edits keys");
        await Assert.That(NativeKeys.Words("⌃⏎ newline · ⏎ goes")).IsEqualTo("Ctrl+Enter newline · Enter goes");
        await Assert.That(NativeKeys.Words(null)).IsEqualTo(string.Empty);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SharpMUTerm.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("SharpMUTerm.slnx not found above " + AppContext.BaseDirectory);
    }
}
