using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Tui;

/// <summary>
/// Which OS credential store this machine has, for <see cref="CredentialVault"/>. In <c>Hosting/</c> because it asks
/// which platform it is on, and that question is a host's. Only the live entry points (<c>Program</c>, the window)
/// call <see cref="ForThisMachine"/>; a snapshot, a test and the parity harness never do, so none of them reads or
/// writes a real keychain.
/// </summary>
internal static class CredentialStores
{
    /// <summary>
    /// The store this machine offers: Windows Credential Manager, the Secret Service through <c>secret-tool</c> on
    /// Linux and the BSDs, the login Keychain through <c>security</c> on macOS. Null when there is none to try — then
    /// <c>secrets.json</c> holds every password, as it always did. A store that is present but does not answer (no
    /// Secret Service in an ssh session) is still returned; its reads say <see cref="CredentialLookup.Unavailable"/>
    /// and the vault falls back per password.
    /// </summary>
    public static ICredentialStore? ForThisMachine()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return ProgramRunner.Find("security") is { } security ? new MacKeychainStore(security) : null;
        }

        return ProgramRunner.Find("secret-tool") is { } secretTool ? new SecretToolStore(secretTool) : null;
    }

    /// <summary>The name an entry is stored under, the same on every platform: <c>SharpMUTerm/&lt;guid&gt;</c>.</summary>
    internal static string TargetFor(Guid id) => $"SharpMUTerm/{id:D}";
}

/// <summary>
/// Windows Credential Manager, through <c>CredWriteW</c>/<c>CredReadW</c>/<c>CredDeleteW</c>: a generic credential per
/// password, named <c>SharpMUTerm/&lt;guid&gt;</c>, kept for the local machine's user (not roamed). Built here, never
/// run by a test; nobody has run it on Windows yet.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsCredentialStore : ICredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string Name => "Windows Credential Manager";

    public CredentialLookup TryRead(Guid id, out string? password)
    {
        password = null;
        try
        {
            if (!CredRead(CredentialStores.TargetFor(id), CredTypeGeneric, 0, out var handle))
            {
                return Marshal.GetLastWin32Error() == ErrorNotFound ? CredentialLookup.Missing : CredentialLookup.Unavailable;
            }

            try
            {
                var credential = Marshal.PtrToStructure<Credential>(handle);
                if (credential.CredentialBlobSize > 0 && credential.CredentialBlob != IntPtr.Zero)
                {
                    password = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
                }

                return string.IsNullOrEmpty(password) ? CredentialLookup.Missing : CredentialLookup.Found;
            }
            finally
            {
                CredFree(handle);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or Win32Exception)
        {
            return CredentialLookup.Unavailable;
        }
    }

    public bool Write(Guid id, string password)
    {
        var blob = Marshal.StringToCoTaskMemUni(password);
        try
        {
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = CredentialStores.TargetFor(id),
                Comment = "A character password saved by SharpMUTerm",
                CredentialBlobSize = (uint)(password.Length * 2),
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName,
            };
            return CredWrite(ref credential, 0);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or Win32Exception)
        {
            return false;
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public bool Delete(Guid id)
    {
        try
        {
            return CredDelete(CredentialStores.TargetFor(id), CredTypeGeneric, 0) || Marshal.GetLastWin32Error() == ErrorNotFound;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or Win32Exception)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}

/// <summary>
/// The freedesktop Secret Service (GNOME Keyring, KWallet, KeePassXC) through libsecret's <c>secret-tool</c>, with the
/// attributes <c>application=sharpmuterm</c> and <c>ref=&lt;guid&gt;</c>. The password goes over the tool's stdin and
/// comes back on its stdout, never on a command line. Exercised against a gnome-keyring daemon on a private D-Bus.
/// <para>
/// <c>secret-tool lookup</c> exits 1 with nothing on stderr when there is no such item, and exits 1 with a message
/// when there is no service ("Cannot autolaunch D-Bus…"): the second is <see cref="CredentialLookup.Unavailable"/>.
/// A locked collection may put up its unlock prompt; the wait is bounded, and a prompt left unanswered reads as
/// unavailable, so the client starts.
/// </para>
/// </summary>
internal sealed class SecretToolStore(string executable) : ICredentialStore
{
    private const string Application = "sharpmuterm";

    public string Name => "the Secret Service keyring";

    public CredentialLookup TryRead(Guid id, out string? password)
    {
        password = null;
        var result = ProgramRunner.Run(executable, ["lookup", "application", Application, "ref", id.ToString("D")], null);
        if (result is null)
        {
            return CredentialLookup.Unavailable;
        }

        if (result.Value.ExitCode == 0)
        {
            password = result.Value.Output;
            return string.IsNullOrEmpty(password) ? CredentialLookup.Missing : CredentialLookup.Found;
        }

        return result.Value.ExitCode == 1 && string.IsNullOrWhiteSpace(result.Value.Error)
            ? CredentialLookup.Missing
            : CredentialLookup.Unavailable;
    }

    public bool Write(Guid id, string password) =>
        ProgramRunner.Run(
            executable,
            ["store", "--label", $"SharpMUTerm character password ({id:D})", "application", Application, "ref", id.ToString("D")],
            password) is { ExitCode: 0 };

    public bool Delete(Guid id)
    {
        var result = ProgramRunner.Run(executable, ["clear", "application", Application, "ref", id.ToString("D")], null);

        // `clear` exits 1 with nothing to say when there was nothing to clear.
        return result is { ExitCode: 0 } || result is { ExitCode: 1, Error: var error } && string.IsNullOrWhiteSpace(error);
    }
}

/// <summary>
/// The macOS login Keychain, through <c>security</c>: a generic password per character, service <c>SharpMUTerm</c>,
/// account the GUID. The write goes through <c>security -i</c> reading its command from stdin, so the password is never
/// on a command line where <c>ps</c> would show it. Built here, never run: nobody has tried it on a Mac.
/// </summary>
internal sealed class MacKeychainStore(string executable) : ICredentialStore
{
    private const string Service = "SharpMUTerm";
    private const int ItemNotFound = 44;

    public string Name => "the macOS Keychain";

    public CredentialLookup TryRead(Guid id, out string? password)
    {
        password = null;
        var result = ProgramRunner.Run(executable, ["find-generic-password", "-s", Service, "-a", id.ToString("D"), "-w"], null);
        if (result is null)
        {
            return CredentialLookup.Unavailable;
        }

        if (result.Value.ExitCode == ItemNotFound)
        {
            return CredentialLookup.Missing;
        }

        if (result.Value.ExitCode != 0)
        {
            return CredentialLookup.Unavailable;
        }

        // `-w` prints the password and a newline.
        password = result.Value.Output.EndsWith('\n') ? result.Value.Output[..^1] : result.Value.Output;
        return string.IsNullOrEmpty(password) ? CredentialLookup.Missing : CredentialLookup.Found;
    }

    public bool Write(Guid id, string password)
    {
        // security -i reads one command per line, with double-quoted words and backslash escapes; a password holding a
        // line break cannot be said that way, and goes to the file instead.
        if (password.Contains('\n') || password.Contains('\r'))
        {
            return false;
        }

        var quoted = password.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var command = $"add-generic-password -U -s {Service} -a {id:D} -l \"SharpMUTerm character password\" -w \"{quoted}\"\n";
        return ProgramRunner.Run(executable, ["-i"], command) is { ExitCode: 0, Error: var error } && string.IsNullOrWhiteSpace(error);
    }

    public bool Delete(Guid id)
    {
        var result = ProgramRunner.Run(executable, ["delete-generic-password", "-s", Service, "-a", id.ToString("D")], null);
        return result is { ExitCode: 0 or ItemNotFound };
    }
}

/// <summary>Runs a credential tool with its arguments as a list (never a shell string), a bounded wait, and stdin.</summary>
internal static class ProgramRunner
{
    /// <summary>
    /// How long a store may take. Long enough to answer an unlock prompt the store puts up; short enough that a store
    /// nobody answers does not hold the client's start for ever.
    /// </summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>The full path of <paramref name="name"/> on <c>PATH</c>, or null.</summary>
    public static string? Find(string name)
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (folder.Length > 0 && Path.Combine(folder, name) is var candidate && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Exit code, stdout and stderr; null when the program could not be started or did not finish in time.</summary>
    public static (int ExitCode, string Output, string Error)? Run(string executable, IReadOnlyList<string> arguments, string? input)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using (var stdin = process.StandardInput)
            {
                if (input is not null)
                {
                    stdin.Write(input);
                }
            }

            if (!process.WaitForExit(Patience))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception)
                {
                }

                return null;
            }

            return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
