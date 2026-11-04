using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Threading;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// What the platform's accessibility API finds on the cells, read through Avalonia's own automation peers in a
/// headless window. Not tried with NVDA, Narrator, VoiceOver or Orca: this proves what the peer says, not what a
/// screen reader then does with it.
/// </summary>
public class OutputAutomationTests
{
    [Test]
    public async Task TheOutputViewIsAReadOnlyDocumentHoldingTheFrontWindowsLines()
    {
        var seen = await TerminalViewTests.Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(10, 4);
            var view = new TerminalView(driver, SKColors.White, SKColors.Black, 16, null);
            var window = new Window { Content = view, Width = 200, Height = 100 };
            window.Show();
            try
            {
                var peer = ControlAutomationPeer.CreatePeerForElement(view);
                var before = (peer.GetName(), peer.GetProvider<IValueProvider>()?.Value);

                var changes = new List<AutomationProperty>();
                peer.PropertyChanged += (_, e) => changes.Add(e.Property);
                driver.Announcer.Reading("Ann", ["You are in the plaza.", "Bea arrives."]);
                Dispatcher.UIThread.RunJobs();

                var value = peer.GetProvider<IValueProvider>()!;
                var threw = false;
                try
                {
                    value.SetValue("typed into the output");
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }

                return (Before: before, Name: peer.GetName(), Type: peer.GetAutomationControlType(), value.IsReadOnly,
                    value.Value, Changed: changes.Contains(ValuePatternIdentifiers.ValueProperty), Threw: threw,
                    Content: peer.IsContentElement());
            }
            finally
            {
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

        await Assert.That(seen.Before.Item1).IsEqualTo("SharpMUTerm output");
        await Assert.That(seen.Before.Item2).IsEqualTo(string.Empty);
        await Assert.That(seen.Name).IsEqualTo("SharpMUTerm output: Ann");
        await Assert.That(seen.Type).IsEqualTo(AutomationControlType.Document);
        await Assert.That(seen.IsReadOnly).IsTrue();
        await Assert.That(seen.Value).IsEqualTo("You are in the plaza.\nBea arrives.");
        await Assert.That(seen.Changed).IsTrue();
        await Assert.That(seen.Threw).IsTrue();
        await Assert.That(seen.Content).IsTrue();
    }

    [Test]
    public async Task TheWindowTitleBarCarriesWhatTheClientSaid()
    {
        var bar = new WindowTitleBar();
        var heard = new List<string>();
        bar.Changed += heard.Add;

        bar.Show("Ann@Mush — SharpMUTerm");

        await Assert.That(bar.Current).IsEqualTo("Ann@Mush — SharpMUTerm");
        await Assert.That(heard).IsEquivalentTo(new[] { "Ann@Mush — SharpMUTerm" });
    }
}
