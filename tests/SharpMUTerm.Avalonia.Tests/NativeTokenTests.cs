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

    [Test]
    public async Task TheDarkAndLightThemesResolveToThePalettesOwnCards()
    {
        await Assert.That(NativeTokens.Dark.Card).IsEqualTo(Color.Parse("#1e1e25"));
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
        Color[] planes = [tokens.Card, tokens.Selected, tokens.Raised];
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
    /// The settings window wears the tokens: its plane is the palette's card and Fluent's accent is the client's, not
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
        await Assert.That(background).IsEqualTo(tokens.Card);
        await Assert.That(accent).IsEqualTo(tokens.Accent);
        await Assert.That(close).IsTrue().Because("the frame has its own close button");
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
