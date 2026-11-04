using System.Reflection;
using SharpMUTerm.Client.Contributions;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// <b>The shared shell draws nothing.</b> Both hosts' views read <c>SharpMUTerm.Client</c>, so the day it names
/// SharpConsoleUI the desktop window inherits the terminal's framework, and the day it names Avalonia the
/// terminal loads a windowing toolkit. Checked over every assembly it pulls in from this repository, so a
/// reference that arrives through Core counts too.
/// </summary>
public class ClientAssemblyTests
{
    // Ordinal: the terminal host's assembly is "sharpmuterm", which a case-blind prefix match finds in every SharpMUTerm.* name.
    private static readonly string[] Forbidden = { "SharpConsoleUI", "Avalonia", "sharpmuterm", "sharpmuterm-window", "SharpMUTerm.Tui" };

    [Test]
    public async Task TheClientAssemblyReferencesNeitherUiFramework()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>();
        pending.Enqueue(typeof(ContributionRegistry).Assembly);
        var offenders = new List<string>();
        while (pending.Count > 0)
        {
            var assembly = pending.Dequeue();
            if (!seen.Add(assembly.GetName().Name!))
            {
                continue;
            }

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name!;
                if (Forbidden.Any(f => name.Equals(f, StringComparison.Ordinal)
                        || name.StartsWith(f + ".", StringComparison.Ordinal)))
                {
                    offenders.Add($"{assembly.GetName().Name} → {name}");
                }

                // Follow this repository's own assemblies; a package's references are its own business.
                if (name.StartsWith("SharpMUTerm.", StringComparison.Ordinal) && !seen.Contains(name))
                {
                    pending.Enqueue(System.Reflection.Assembly.Load(reference));
                }
            }
        }

        await Assert.That(offenders).IsEmpty();
        await Assert.That(seen).Contains("SharpMUTerm.Core");
    }
}
