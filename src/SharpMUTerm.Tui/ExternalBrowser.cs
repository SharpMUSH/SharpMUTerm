using System.Diagnostics;

namespace SharpMUTerm.Tui;

/// <summary>
/// Hands a URL to whatever the desktop has registered for it — the browser, in practice. Split from
/// <see cref="SharpMUTermApp"/> so the gate can be asserted without a window system, and so the one
/// place in this client that starts another process is a file you can read in a minute.
/// </summary>
internal static class ExternalBrowser
{
    /// <summary>
    /// Whether <paramref name="target"/> is something this client will hand to the desktop, and its
    /// canonical form if so. Absolute, and <c>http</c> or <c>https</c> — nothing else, ever.
    /// <para>
    /// The output is <see cref="Uri.AbsoluteUri"/> rather than the string that came in, so what is
    /// launched is what .NET parsed rather than a second reading of the same bytes. It is the pairing
    /// that goes wrong in this kind of code: validating one form and passing another.
    /// </para>
    /// </summary>
    internal static bool TryParseOpenable(string? target, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        if (!Uri.TryCreate(target.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }

    /// <summary>
    /// The live launcher, handed to the app by <c>Program</c> and by nothing else.
    /// <para>
    /// <c>UseShellExecute</c> is what routes this through the desktop's own handler — <c>xdg-open</c> on
    /// Linux, the shell's URL association on Windows — and the URL goes in
    /// <see cref="ProcessStartInfo.FileName"/> as a single argument. It is never composed into a command
    /// string for a shell to re-split: that is how a URL's <c>&amp;</c> or <c>;</c> becomes a second
    /// command, and this input comes off a socket.
    /// </para>
    /// <para>
    /// <strong>The gate is re-checked here, and that is not redundant.</strong>
    /// <c>SharpMUTermApp.OpenExternally</c> validates before it calls this, and that is where a refusal
    /// gets to be a message the reader sees — but the property worth having is that <em>this method</em>
    /// cannot launch anything but an <c>http(s)</c> URL, whoever calls it and however the call site is
    /// refactored later. A scheme gate one caller away from the process launch is a gate somebody can
    /// walk around without noticing; one in the same function is a fact about the function.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The target is not an absolute <c>http</c> or <c>https</c> URL. Thrown rather than ignored: the app
    /// catches it and says so, and a launcher that silently did nothing would be indistinguishable from a
    /// desktop with no browser registered.
    /// </exception>
    internal static void Open(string url)
    {
        if (!TryParseOpenable(url, out var target))
        {
            throw new ArgumentException($"not an http or https URL: {url}", nameof(url));
        }

        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    }
}
