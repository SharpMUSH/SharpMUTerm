using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Simple;

namespace SharpMUTerm.Avalonia;

internal sealed class App : Application
{
    /// <summary>Set by <see cref="Program"/> before the lifetime starts.</summary>
    public static WindowOptions Options { get; set; } = WindowOptions.Parse([]);

    public override void Initialize() => Styles.Add(new SimpleTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The client decides when the program ends: closing the window asks it to quit, and it is the
            // client's loop ending that shuts the application down.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MainWindow(Options, desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
