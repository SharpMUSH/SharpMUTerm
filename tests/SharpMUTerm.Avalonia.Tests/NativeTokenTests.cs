using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window has one design language (<see cref="NativeTokens"/>): its planes, inks, radii and type sizes come from one
/// place, derived from the client's theme, and every ink clears the text floor on the planes it is drawn on. The audit
/// that asked for it counted 15 font sizes, 8 radii and 33 literal colours across the window's views, and a Fluent blue
/// settings window beside a teal palette.
/// </summary>
[NotInParallel]
public class NativeTokenTests
{
    /// <summary>
    /// Files allowed a literal size or colour, and why. Nothing else in the window's assembly may write one: a new view
    /// takes its sizes from the ramp and its colours from the tokens, or it is a second design language again.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["NativeTokens.cs"] = "the tokens themselves",
    };

    [Test]
    public async Task NoViewWritesALiteralFontSizeOrColour()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "SharpMUTerm.Avalonia");
        var size = new Regex(@"\bFontSize\s*=\s*[0-9]", RegexOptions.Compiled);
        var colour = new Regex("Color\\.Parse\\(\"#", RegexOptions.Compiled);
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly))
        {
            if (Allowed.ContainsKey(Path.GetFileName(file)))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (size.IsMatch(line) || colour.IsMatch(line))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line}");
                }
            }
        }

        await Assert.That(offenders).IsEmpty().Because(string.Join("\n", offenders));
    }

    /// <summary>
    /// Files allowed a literal margin, padding or spacing, and why. Everything else takes its insets from the scale
    /// (<c>NativeTokens.Space*</c>, <c>PanelInset</c>, <c>ModalInset</c>), so the native views share one set of insets.
    /// </summary>
    private static readonly Dictionary<string, string> SpacingAllowed = new(StringComparer.Ordinal)
    {
        ["NativeTokens.cs"] = "the tokens themselves",
        ["NativeTokens.Page.cs"] = "the tokens themselves",
        ["NativeTokens.Spacing.cs"] = "the scale itself",
        ["TerminalView.cs"] = "the cells' own view: its few insets are the grid's and the field's (NativeLayout), not a view's",
        ["NativeFooter.cs"] = "the status footer, laid out from NativeLayout's footer constants in its own pass",
        ["NoticeToasts.cs"] = "the toasts, laid out from their own card constants in their own pass",
    };

    [Test]
    public async Task NoViewWritesALiteralMarginPaddingOrSpacing()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "SharpMUTerm.Avalonia");
        var thickness = new Regex(@"new Thickness\(([^()]*)\)", RegexOptions.Compiled);
        var literal = new Regex(@"(?<![\w.])[1-9][0-9]*(\.[0-9]+)?(?![\w.])", RegexOptions.Compiled);
        var spacing = new Regex(@"(?<!const double )\bSpacing\s*=\s*[1-9]", RegexOptions.Compiled);
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly))
        {
            if (SpacingAllowed.ContainsKey(Path.GetFileName(file)))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (spacing.IsMatch(line) || thickness.Matches(line).Any(m => literal.IsMatch(m.Groups[1].Value)))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line}");
                }
            }
        }

        await Assert.That(offenders).IsEmpty().Because(string.Join("\n", offenders));
    }

    [Test]
    public async Task TheSpacingScaleAndTheChromeSizesAreWholeDevicePixelsAtWindowsScales()
    {
        // 4 logical pixels are whole device pixels at 100, 125, 150 and 200%, so a chrome size on that grid puts the cells
        // on a device pixel at every scale Windows offers, and nothing placed from a cell origin lands on a blended row.
        double[] chrome = [NativeLayout.HeaderHeight, NativeLayout.SidebarWidth, NativeLayout.SidebarSlim, NativeLayout.SidebarGutter,
            NativeLayout.StatusHeight, NativeLayout.FieldStrip];
        foreach (var size in chrome)
        {
            await Assert.That(size % 4).IsEqualTo(0).Because($"{size} must be a multiple of 4");
        }

        double[] scale = [NativeTokens.Space2, NativeTokens.Space4, NativeTokens.Space6, NativeTokens.Space8, NativeTokens.Space10,
            NativeTokens.Space12, NativeTokens.Space14, NativeTokens.Space16, NativeTokens.Space20, NativeTokens.Space24];
        foreach (var step in scale)
        {
            await Assert.That(step % 2).IsEqualTo(0).Because("no odd step: an odd logical pixel is half a device pixel at 150%");
        }
    }

    [Test]
    public async Task TheDarkAndLightThemesResolveToThePalettesOwnCards()
    {
        await Assert.That(NativeTokens.Dark.Card).IsEqualTo(Color.Parse("#171b24")).Because("the mockup's panel plane");
        await Assert.That(NativeTokens.Dark.Well).IsEqualTo(Color.Parse("#10131a")).Because("the mockup's ground, which its inputs sit in");
        await Assert.That(NativeTokens.Dark.Selected).IsEqualTo(Color.Parse("#1f3a3a")).Because("the mockup's selected row");
        await Assert.That(NativeTokens.Dark.Accent).IsEqualTo(Color.Parse("#00f5b7")).Because("the client's own teal, already legible on a dark card");
        await Assert.That(NativeTokens.Light.Card).IsEqualTo(Colors.White);
        await Assert.That(NativeTokens.Dark.IsDark).IsTrue();
        await Assert.That(NativeTokens.Light.IsDark).IsFalse();
    }

    [Test]
    public async Task SolarizedGetsItsOwnHueNotTheDarkThemesGrey()
    {
        var solarized = NativeTokens.For(ThemeLibrary.SolarizedDark());
        await Assert.That(solarized.Card).IsNotEqualTo(NativeTokens.Dark.Card);
        await Assert.That(solarized.Card.B).IsGreaterThan(solarized.Card.R).Because("Solarized's background is a blue-green");
    }

    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task EveryInkClearsTheTextFloorOnEveryPlaneItIsDrawnOn(string name)
    {
        var tokens = NativeTokens.For(ThemeLibrary.Get(name));
        Color[] planes = [tokens.Card, tokens.Selected, tokens.Raised, tokens.Well];
        var misses = new List<string>();
        foreach (var (ink, label) in new[]
        {
            (tokens.Text, "text"), (tokens.Dim, "dim"), (tokens.Header, "header"), (tokens.Accent, "accent"),
            (tokens.Warn, "warn"), (tokens.Notice, "notice"),
        })
        {
            foreach (var plane in planes)
            {
                var ratio = Contrast.Ratio(Rgb(ink), Rgb(plane));
                if (ratio < NativeTokens.TextFloor)
                {
                    misses.Add($"{label} {ink} on {plane}: {ratio:0.00}");
                }
            }
        }

        misses.AddRange(Contrast.Ratio(Rgb(tokens.AccentInk), Rgb(tokens.Accent)) < NativeTokens.TextFloor ? ["accent ink"] : []);
        misses.AddRange(Contrast.Ratio(Rgb(tokens.KeyInk), Rgb(tokens.KeyFill)) < NativeTokens.TextFloor ? ["key ink"] : []);
        await Assert.That(misses).IsEmpty().Because(string.Join("\n", misses));
    }

    /// <summary>
    /// The native header's and sidebar's secondary text (<c>offline</c>, <c>Alt+2</c>, <c>CONNECTIONS</c>) is small
    /// text, held to <see cref="NativeTokens.TextFloor"/> on the planes the client hands the chrome — the sidebar's
    /// backdrop and the header's status plane. It was the ink at two-thirds alpha, 3.1–3.4:1 on Light's sidebar (UX audit
    /// wave 11, #7); and Light's backdrop itself is no longer mid-grey.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task TheChromesSecondaryTextIsBodyText(string name)
    {
        var theme = ThemeLibrary.Get(name);
        var ink = theme.Resolve(SharpMUTerm.Core.Text.TerminalColor.Default, isBackground: false);
        foreach (var plane in new[] { SharpMUTerm.Tui.WorkspacePalette.Backdrop(theme), theme.StatusBackground })
        {
            var dim = SharpMUTerm.Avalonia.NativeChromeView.SecondaryInk(
                Color.FromRgb(ink.R, ink.G, ink.B), Color.FromRgb(plane.R, plane.G, plane.B));
            await Assert.That(Contrast.Ratio(Rgb(dim), plane)).IsGreaterThanOrEqualTo(NativeTokens.TextFloor)
                .Because($"{name}: {dim} on {plane.ToHex()}");
        }

        if (name == "Light")
        {
            var backdrop = SharpMUTerm.Tui.WorkspacePalette.Backdrop(theme);
            await Assert.That(Contrast.RelativeLuminance(backdrop)).IsGreaterThan(0.7)
                .Because($"Light's rail and status plane {backdrop.ToHex()} read as disabled at mid-grey");
        }
    }

    /// <summary>
    /// The settings window wears the tokens: its plane is the page tokens' ground and Fluent's accent is the client's, not
    /// Fluent's own blue (<c>#60cdff</c>/<c>#005fb8</c>) — and it follows a Light theme the same way.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    public async Task TheSettingsWindowWearsTheTokens(string name)
    {
        var theme = ThemeLibrary.Get(name);
        var (background, accent, close) = await TerminalViewTests.Session.Dispatch(() =>
        {
            var config = new AppConfiguration();
            var form = new SettingsForm(BuiltInSettings.All, config, c => c(), _ => { }, "text", isDark: name != "Light")
            {
                Theme = () => theme,
            };
            var window = new SettingsWindow(form);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                return (((ISolidColorBrush)window.Background!).Color, (Color)window.Resources["SystemAccentColor"]!, window.CloseButton.IsVisible);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        var tokens = NativeTokens.For(theme);
        await Assert.That(background).IsEqualTo(tokens.Page.Ground).Because("a page stands on the page tokens' ground");
        await Assert.That(accent).IsEqualTo(tokens.Accent);
        await Assert.That(close).IsTrue().Because("the frame has its own close button");
    }

    /// <summary>
    /// The page tokens are the settings mockup's on Dark, and on every built-in theme each page ink clears the floor on
    /// every page plane it can land on (and the search mark's ink on the mark).
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task ThePageInksClearTheFloorOnEveryPagePlane(string name)
    {
        var page = NativeTokens.For(ThemeLibrary.Get(name)).Page;
        static Rgb Of(Color c) => new(c.R, c.G, c.B);
        foreach (var plane in new[] { page.Ground, page.Nav, page.Card, page.Field, page.Button, page.Popup, page.PopupSelected })
        {
            foreach (var ink in new[] { page.Text, page.Dim, page.Link })
            {
                await Assert.That(Contrast.Ratio(Of(ink), Of(plane))).IsGreaterThanOrEqualTo(NativeTokens.TextFloor)
                    .Because($"{name}: {ink} on {plane}");
            }
        }

        await Assert.That(Contrast.Ratio(Of(page.MarkInk), Of(page.Mark))).IsGreaterThanOrEqualTo(NativeTokens.TextFloor);
        if (name == "Dark")
        {
            await Assert.That(page.Ground).IsEqualTo(Color.Parse("#10131a")).Because("the mockup's ground");
            await Assert.That(page.Card).IsEqualTo(Color.Parse("#171b24")).Because("the mockup's card");
            await Assert.That(page.Rule).IsEqualTo(Color.Parse("#2c3447")).Because("the mockup's rule");
        }
    }

    /// <summary>
    /// The settings window is a modal over the main window: a scrim lies over it while it is open (a press on it closes
    /// the settings) and goes with it. Opened beside the output — the trigger builder — it is docked to the main window's
    /// right half with no scrim, so the line the rule was made from stays in view.
    /// </summary>
    [Test]
    public async Task TheSettingsAreAModalUnlessTheyOpenBesideTheOutput()
    {
        var (veiled, unveiled, besideScrim, left, width, mainWidth) = await TerminalViewTests.Session.Dispatch(() =>
        {
            var presenter = new SettingsPresenter();
            var root = new Panel();
            var main = new Window { Content = root, Width = 1400, Height = 700 };
            presenter.Owner = main;
            main.Show();
            var config = new AppConfiguration();
            try
            {
                var window = presenter.ShowNow(new SettingsForm(BuiltInSettings.All, config, c => c(), _ => { }, "text", isDark: true));
                Dispatcher.UIThread.RunJobs();
                var veiled = presenter.Scrim is { } scrim && root.Children.Contains(scrim);
                window.Close();
                Dispatcher.UIThread.RunJobs();
                var unveiled = presenter.Scrim is null && root.Children.Count == 0;

                var beside = presenter.ShowNow(new SettingsForm(BuiltInSettings.All, config, c => c(), _ => { }, "triggers", isDark: true)
                {
                    BesideOutput = true,
                });
                Dispatcher.UIThread.RunJobs();
                var result = (veiled, unveiled, presenter.Scrim is not null, beside.Position.X - main.Position.X, beside.Width, main.ClientSize.Width);
                beside.Close();
                return result;
            }
            finally
            {
                main.Close();
            }
        }, CancellationToken.None);

        await Assert.That(veiled).IsTrue().Because("the main window is under a scrim while the settings are open");
        await Assert.That(unveiled).IsTrue().Because("the scrim goes with the settings");
        await Assert.That(besideScrim).IsFalse().Because("beside the output nothing covers the output");
        await Assert.That(width).IsEqualTo(mainWidth / 2).Because("docked to the right half");
        await Assert.That((double)left).IsEqualTo(mainWidth / 2).Within(1);
    }

    private static Rgb Rgb(Color colour) => new(colour.R, colour.G, colour.B);

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
