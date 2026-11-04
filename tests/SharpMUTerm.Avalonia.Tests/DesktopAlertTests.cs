using System.Text;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// What an alert does on each desktop, read through a fake of everything that reaches the machine: which program
/// starts with which arguments, when the taskbar flashes, and when the X11 urgency hint goes up and comes down. The
/// real notifiers, the toast and the hint are not exercised here (see CLAUDE.md for what has been seen on a desktop).
/// </summary>
public class DesktopAlertTests
{
    private static readonly AttentionRequest Page = new("Corvid@Aetherfall", "Rivane pages: are you there?");

    private sealed class FakeNative : IDesktopNative
    {
        public List<AlertLaunch> Launched { get; } = [];

        public List<IntPtr> Flashed { get; } = [];

        public List<(IntPtr Window, bool Urgent)> Urgency { get; } = [];

        public bool ToastRegistered { get; set; }

        public bool UrgencyWorks { get; set; } = true;

        public void Launch(AlertLaunch launch) => Launched.Add(launch);

        public void Flash(IntPtr hwnd) => Flashed.Add(hwnd);

        public bool SetUrgency(IntPtr xid, bool urgent)
        {
            Urgency.Add((xid, urgent));
            return UrgencyWorks;
        }
    }

    [Test]
    public async Task OnX11TheWindowIsMarkedUrgentAndActivationTakesItBack()
    {
        var native = new FakeNative();
        var alert = new DesktopAlert(DesktopPlatform.Linux, native);

        alert.Show(Page, 0x2a00007, "XID");

        await Assert.That(native.Urgency).IsEquivalentTo([((IntPtr)0x2a00007, true)]);
        await Assert.That(alert.Urgent).IsTrue();
        await Assert.That(native.Launched.Single().Program).IsEqualTo("notify-send");
        await Assert.That(native.Launched.Single().Arguments)
            .IsEquivalentTo(["--app-name=SharpMUTerm", "--", Page.Title, Page.Body]);

        alert.Clear();
        alert.Clear();
        await Assert.That(native.Urgency).IsEquivalentTo([((IntPtr)0x2a00007, true), ((IntPtr)0x2a00007, false)]);
        await Assert.That(alert.Urgent).IsFalse();
        await Assert.That(native.Flashed).IsEmpty();
    }

    [Test]
    public async Task AWindowThatIsNotAnX11WindowGetsNoHintAndNothingIsTakenBack()
    {
        var native = new FakeNative();
        var alert = new DesktopAlert(DesktopPlatform.Linux, native);

        alert.Show(Page, 0x10, "Wayland");
        alert.Show(Page, null, null);
        alert.Clear();

        await Assert.That(native.Urgency).IsEmpty();
        await Assert.That(native.Launched.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AHintThatCouldNotBeSetIsNotTakenBack()
    {
        var native = new FakeNative { UrgencyWorks = false };
        var alert = new DesktopAlert(DesktopPlatform.Linux, native);

        alert.Show(Page, 0x2a00007, "XID");
        alert.Clear();

        await Assert.That(native.Urgency.Count).IsEqualTo(1);
    }

    [Test]
    public async Task OnWindowsTheTaskbarFlashesAndAToastGoesOnlyWhereTheInstallerRegisteredTheId()
    {
        var portable = new FakeNative();
        new DesktopAlert(DesktopPlatform.Windows, portable).Show(Page, 0x1234, "HWND");
        await Assert.That(portable.Flashed).IsEquivalentTo([(IntPtr)0x1234]);
        await Assert.That(portable.Launched).IsEmpty();

        var installed = new FakeNative { ToastRegistered = true };
        new DesktopAlert(DesktopPlatform.Windows, installed).Show(Page, 0x1234, "HWND");
        await Assert.That(installed.Flashed.Count).IsEqualTo(1);
        var toast = installed.Launched.Single();
        await Assert.That(toast.Program).IsEqualTo("powershell.exe");
        await Assert.That(toast.Arguments.Take(toast.Arguments.Count - 1))
            .IsEquivalentTo(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-EncodedCommand"]);
        await Assert.That(installed.Urgency).IsEmpty();
    }

    [Test]
    public async Task TheToastScriptCarriesTheTextOnlyAsBase64()
    {
        const string hostile = "'; Remove-Item C:\\ -Recurse; '<text>&amp;</text>\"$(whoami)\"";
        var launch = WindowsToast.Launch(WindowsAppId.Value, "Corvid", hostile);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(launch.Arguments[^1]));

        await Assert.That(script).IsEqualTo(WindowsToast.Script(WindowsAppId.Value, "Corvid", hostile));
        await Assert.That(script).DoesNotContain("Remove-Item");
        await Assert.That(script).DoesNotContain("whoami");
        await Assert.That(script).Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(hostile)));
        await Assert.That(script).Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("SharpMUTerm.Window")));
        await Assert.That(script).Contains("CreateToastNotifier");
        await Assert.That(script).Contains("CreateTextNode");
    }

    [Test]
    public async Task OnMacOSTheTextIsArgvToAFixedScript()
    {
        var native = new FakeNative();
        new DesktopAlert(DesktopPlatform.MacOS, native).Show(Page, 0x55, "NSWindow");

        var launch = native.Launched.Single();
        await Assert.That(launch.Program).IsEqualTo("/usr/bin/osascript");
        await Assert.That(launch.Arguments[^2]).IsEqualTo(Page.Title);
        await Assert.That(launch.Arguments[^1]).IsEqualTo(Page.Body);
        await Assert.That(native.Flashed).IsEmpty();
        await Assert.That(native.Urgency).IsEmpty();
    }

    [Test]
    public async Task TheTextIsOneLineAndBounded()
    {
        var request = new AttentionRequest("A\u001b]0;x\u0007", new string('z', 1000) + "\nmore");
        var launch = DesktopAlert.Launches(DesktopPlatform.Linux, request, toastRegistered: false).Single();

        await Assert.That(launch.Arguments[^2]).IsEqualTo("A ]0;x ");
        await Assert.That(launch.Arguments[^1].Length).IsEqualTo(DesktopAlert.MaxBody);
        await Assert.That(DesktopAlert.Bound("ab\U0001F600", 3)).IsEqualTo("ab");
    }
}
