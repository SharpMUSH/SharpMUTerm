using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Tui;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window's keyboard and screen-reader pass: every native surface a player can tab through — the settings window,
/// ⌃P, each native modal, the welcome page, the composer — is opened over the demo in a real window, and every control on
/// it that can take the keyboard must be named for a screen reader, wear the tokens' focus ring when the keyboard reaches
/// it (<see cref="FocusRing"/>), and be reached by Tab from the first one without Tab leaving the surface.
/// </summary>
[NotInParallel]
public class NativeFocusAuditTests
{
    private sealed record Rig(Window Window, Panel Root, TerminalView View, DesktopLayer Layer, SharpMUTermApp App, AvaloniaConsoleDriver Driver);

    private static Task<T> Run<T>(Func<Rig, T> body, string? view = null, string theme = "Dark") =>
        TerminalViewTests.Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(120, 34);
            var terminal = new TerminalView(driver, SKColors.White, new SKColor(0x1e, 0x1e, 0x24), 14, null);
            var root = new Panel();
            root.Children.Add(terminal);
            var layer = new DesktopLayer(terminal, HostChrome.HeaderAndRail);
            root.Children.Add(layer);
            driver.Composer.Attach(root, layer, terminal, () => terminal.Focus());
            driver.Palette.Attach(root, () => terminal.Focus());
            var window = new Window { Content = root, Width = 1200, Height = 760 };
            driver.Modals.Attach(window, root, () => terminal.Focus(), driver.RaiseKey);
            driver.Settings.Owner = window;
            window.Show();
            terminal.Focus();
            var config = DemoScene.Build();
            config.ThemeName = theme;
            config.Theme = theme == "Light" ? Core.Theming.ThemeLibrary.Light() : Core.Theming.ThemeLibrary.Dark();
            var app = new SharpMUTermApp(config, WindowHost.Create(driver, clipboard: null) with
            {
                Audio = null,
                Presence = null,
                Announcer = null,
                RunsInline = true,
            });
            driver.KeyPressed += (_, key) => app.SimulateKey(key);
            try
            {
                app.RenderSnapshot(view);
                DesktopLayerTests.Drain();
                return body(new Rig(window, root, terminal, layer, app, driver));
            }
            finally
            {
                driver.Settings.Window?.Close();
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

    private static ConsoleKeyInfo Fn(ConsoleKey key) => new('\0', key, false, false, false);

    /// <summary>The controls on <paramref name="root"/> that can take the keyboard now.</summary>
    internal static List<Control> Focusables(Visual root) =>
        root.GetVisualDescendants().Prepend(root).OfType<Control>()
            .Where(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.Bounds is { Width: > 0, Height: > 0 })
            .ToList();

    /// <summary>What a screen reader would call <paramref name="control"/>: its automation name, else its peer's.</summary>
    internal static string? SpokenName(Control control)
    {
        if (AutomationProperties.GetName(control) is { Length: > 0 } name)
        {
            return name;
        }

        try
        {
            // A content control's peer falls back on its content's ToString, which for a panel is a type's name: that is
            // what a screen reader would say, and it says nothing.
            return ControlAutomationPeer.CreatePeerForElement(control).GetName() is { Length: > 0 } peer
                && !peer.StartsWith("Avalonia.", StringComparison.Ordinal) ? peer : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every way <paramref name="root"/> fails the pass, prefixed with <paramref name="surface"/>: a focusable control with
    /// no name, one with no focus ring, a tab stop Tab never reaches, and Tab leaving the surface.
    /// </summary>
    internal static List<string> Audit(string surface, Visual root)
    {
        var found = new List<string>();
        var focusables = Focusables(root);
        if (focusables.Count == 0)
        {
            found.Add($"{surface}: nothing on it takes the keyboard");
            return found;
        }

        foreach (var control in focusables)
        {
            var what = $"{surface}: {control.GetType().Name} \"{SpokenName(control) ?? Describe(control)}\"";
            if (SpokenName(control) is null)
            {
                found.Add($"{what} has no name a screen reader can say");
            }
            else if (NativeKeys.HasGlyph(SpokenName(control)!))
            {
                found.Add($"{what} spells a key in glyphs");
            }

            if (control.FocusAdorner?.Build() is not FocusRing)
            {
                found.Add($"{what} has no focus ring");
            }
        }

        var stops = focusables.Where(c => KeyboardNavigation.GetIsTabStop(c)).ToList();
        if (stops.Count > 0 && TopLevel.GetTopLevel(root) is { } top)
        {
            // Real Tab presses on the window, read back off its focus manager: the walk a player's keyboard makes.
            // From nothing focused, so arriving at the first stop is the keyboard's arrival and shows the ring.
            top.FocusManager?.Focus(null, NavigationMethod.Unspecified);
            DesktopLayerTests.Drain();
            stops[0].Focus(NavigationMethod.Tab);
            DesktopLayerTests.Drain();
            var reached = new HashSet<Control>();
            var ringed = false;
            for (var step = 0; step < (stops.Count * 3) + 8; step++)
            {
                if (top.FocusManager?.GetFocusedElement() is not Control at)
                {
                    found.Add($"{surface}: Tab left nothing focused");
                    break;
                }

                if (!ReferenceEquals(at, root) && !root.IsVisualAncestorOf(at))
                {
                    found.Add($"{surface}: Tab leaves the surface for {at.GetType().Name} \"{SpokenName(at) ?? Describe(at)}\"");
                    break;
                }

                ringed |= AdornerLayer.GetAdornerLayer(at) is { } layer && layer.Children.OfType<FocusRing>().Any(r => r.IsVisible);
                if (!reached.Add(at) && reached.Count > 1)
                {
                    break;
                }

                top.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                top.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                DesktopLayerTests.Drain();
            }

            // A list, a tab strip — any container Tab enters once — is reached when any of its items is; arrows move inside.
            foreach (var stop in stops.Where(s => !reached.Contains(s) && !reached.Any(r => SharesOnceContainer(s, r, root))))
            {
                found.Add($"{surface}: Tab never reaches {stop.GetType().Name} \"{SpokenName(stop) ?? Describe(stop)}\"");
            }

            if (!ringed)
            {
                found.Add($"{surface}: no focus ring was drawn while tabbing");
            }
        }

        return found;
    }

    private static bool SharesOnceContainer(Control a, Control b, Visual root)
    {
        foreach (var ancestor in a.GetVisualAncestors().OfType<InputElement>())
        {
            if (ReferenceEquals(ancestor, root))
            {
                return false;
            }

            if (KeyboardNavigation.GetTabNavigation(ancestor) is KeyboardNavigationMode.Once && ancestor.IsVisualAncestorOf(b))
            {
                return true;
            }
        }

        return false;
    }

    private static string Describe(Control control) =>
        control.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => t is { Length: > 0 })
        ?? control.Name ?? "?";

    private static Visual? OpenModal(Rig rig) => (Visual?)rig.Driver.Modals.Quit ?? rig.Driver.Modals.Modal;

    private static void CloseModals(Rig rig)
    {
        while ((rig.Driver.Modals.Quit ?? rig.Driver.Modals.Modal) is { } modal)
        {
            modal.Session.Dismiss();
            rig.Driver.Modals.Close(modal.Session);
            DesktopLayerTests.Drain();
        }
    }

    private static List<string> AuditEverySurface(Rig rig)
    {
        var found = new List<string>();

        Key(rig, Ctrl('p', ConsoleKey.P));
        if (rig.Driver.Palette.Overlay is { } palette)
        {
            found.AddRange(Audit("palette", palette));
            Key(rig, Ctrl('p', ConsoleKey.P));
        }
        else
        {
            found.Add("palette: did not open natively");
        }

        Key(rig, Fn(ConsoleKey.F1));
        if (rig.Driver.Composer.Overlay is { } composer)
        {
            found.AddRange(Audit("composer", composer));
            Key(rig, Fn(ConsoleKey.F1));
        }
        else
        {
            found.Add("composer: did not open natively");
        }

        foreach (var (name, open) in new (string, Action)[]
        {
            ("search", () => Key(rig, Ctrl('f', ConsoleKey.F))),
            ("history", () => Key(rig, Ctrl('r', ConsoleKey.R))),
            ("layouts", () => Key(rig, new ConsoleKeyInfo('m', ConsoleKey.M, false, true, false))),
            ("help", () => rig.App.DispatchCommand(BuiltInCommands.HelpCommand)),
            ("welcome", () => rig.App.DispatchCommand(BuiltInCommands.WelcomeCommand)),
            ("browse worlds", () => rig.App.DispatchCommand(BuiltInCommands.BrowseWorldsCommand)),
            ("dictionary offer", () => rig.App.DispatchCommand(BuiltInCommands.DictionaryCommand)),
            ("quit", () => Key(rig, Ctrl('q', ConsoleKey.Q))),
        })
        {
            open();
            DesktopLayerTests.Drain();
            if (OpenModal(rig) is { } modal)
            {
                found.AddRange(Audit(name, modal));
            }
            else
            {
                found.Add($"{name}: did not open natively");
            }

            CloseModals(rig);
        }

        foreach (var (name, key) in new[]
        {
            ("settings F7", ConsoleKey.F7),
            ("settings F2", ConsoleKey.F2),
            ("settings F5", ConsoleKey.F5),
            ("settings F10", ConsoleKey.F10),
        })
        {
            Key(rig, Fn(key));
            if (rig.Driver.Settings.Window is { } settings)
            {
                found.AddRange(Audit(name, settings));
                settings.Close();
                DesktopLayerTests.Drain();
            }
            else
            {
                found.Add($"{name}: did not open natively");
            }
        }

        return found;
    }

    [Test]
    public async Task EveryNativeSurfaceIsNamedRingedAndReachedByTab()
    {
        var found = await Run(AuditEverySurface);
        await Assert.That(found).IsEmpty().Because(string.Join("\n", found));
    }

    [Test]
    public async Task EveryNativeSurfaceIsNamedRingedAndReachedByTabOnLight()
    {
        var found = await Run(AuditEverySurface, theme: "Light");
        await Assert.That(found).IsEmpty().Because(string.Join("\n", found));
    }

    [Test]
    public async Task TheDockedComposerIsNamedAndRinged()
    {
        var found = await Run(rig =>
        {
            var composers = rig.Layer.GetVisualDescendants().OfType<ComposerView>().ToList();
            return composers.Count == 0
                ? ["docked composer: not drawn natively in panel-composer"]
                : composers.SelectMany(c => Audit("docked composer", c)).ToList();
        }, view: "panel-composer");
        await Assert.That(found).IsEmpty().Because(string.Join("\n", found));
    }

    /// <summary>
    /// The views over the panes that deliberately never take the keyboard (the keyboard stays on the command line; their
    /// doors are ⌃P's and the client's keys) are still named, so a screen reader exploring the window can say what each is.
    /// </summary>
    [Test]
    public async Task TheViewsThatNeverTakeTheKeyboardAreStillNamed()
    {
        var found = await Run(rig =>
        {
            rig.App.SimulateNotice("A notice", Core.Diagnostics.MessageSeverity.Info, null, null);
            DesktopLayerTests.Drain();
            rig.Driver.Flush();
            DesktopLayerTests.Drain();
            var found = new List<string>();
            foreach (var view in rig.Layer.GetVisualDescendants().OfType<Control>()
                .Where(c => c is NativeHeader or NativeSidebar or NativeTabStrip or NoticeToasts or QuickBarButtons or WhoHereList
                    or VitalsGauges or MapCanvas))
            {
                if (SpokenName(view) is null)
                {
                    found.Add($"{view.GetType().Name} has no name");
                }

                found.AddRange(Focusables(view).Select(f => $"{view.GetType().Name}: {f.GetType().Name} takes the keyboard"));
            }

            return found;
        }, view: "layout-combat");
        await Assert.That(found).IsEmpty().Because(string.Join("\n", found));
    }
}
