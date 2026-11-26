using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Diagnostics;

namespace SharpMUTerm.Tui;

/// <summary>
/// ⌃1–⌃9 and ⌃0: go straight to the main window of the first ten open characters, in the order the sidebar lists
/// them (<see cref="CommandCatalog.CharacterCycle"/>, the order ⌥J and ⌥K walk).
/// <para>
/// <b>Only where the host delivers Ctrl+digit as itself</b> (<see cref="HostKeys.ReportsChordsWhole"/>: the window).
/// A terminal writes ⌃1 as a bare 1, ⌃3 as Escape and ⌃8 as Backspace (<c>MacroKeys.DigitBytes</c>), so there the
/// chord does not exist and nothing claims it; ⌥J/⌥K and ⌃P's <c>Switch to …</c> entries are the way, and ⌥1–⌥9 are
/// still the active character's windows in both hosts.
/// </para>
/// <para>
/// <b>Open characters only</b>, the cycle's rule: <see cref="SwitchToCharacter"/> opens a session for one that has none,
/// and a digit that dialled a character by accident is the thing the cycle was built not to do. The character you are
/// on counts, so ⌃1 from its Chat window goes back to its main window.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Whether this host's keyboard has the character chords (the window, not a terminal).</summary>
    private bool HasCharacterJumps => Host.Keys.ReportsChordsWhole;

    /// <summary>
    /// Claims ⌃0–⌃9 where the host reports them whole. Registered apart from <see cref="MacroKeys.AppShortcuts"/>
    /// for the reason <see cref="MacroKeys.CharacterJumpNumber"/> gives; F4 reads <see cref="MacroKeys.Verdict"/>, which
    /// says taken on exactly this host.
    /// </summary>
    private void RegisterCharacterJumps()
    {
        if (!HasCharacterJumps)
        {
            return;
        }

        for (var key = ConsoleKey.D0; key <= ConsoleKey.D9; key++)
        {
            var number = MacroKeys.CharacterJumpNumber(key)!.Value;
            Func<bool> action = () =>
            {
                DisarmPrefix();
                LeaveRailKeys();
                JumpToCharacter(number);
                return true;
            };
            _system.RegisterGlobalShortcut(ConsoleModifiers.Control, key, action);
            _shortcuts[(ConsoleModifiers.Control, key)] = action;
        }
    }

    /// <summary>Goes to the main window of open character <paramref name="number"/> (1-based), or says why it cannot.</summary>
    private void JumpToCharacter(int number)
    {
        var cycle = CommandCatalog.CharacterCycle(BuildCharacterRefs());
        if (number < 1 || number > cycle.Count)
        {
            // Never silent, ⌥N's rule: the count is the whole answer.
            Notice(
                cycle.Count switch
                {
                    0 => "no character is open — the sidebar and ⌃P open one",
                    1 => $"there is no character {number} — only {DisplayName(cycle[0].SessionKey)} is open",
                    _ => $"there is no character {number} — {cycle.Count} are open",
                },
                MessageSeverity.Warning,
                CommandIds.CharacterJumpChord(number));
            return;
        }

        SwitchToCharacter(cycle[number - 1].SessionKey);
    }

    /// <summary>
    /// The sidebar chord for each of the first ten open characters where the host has the character chords: every
    /// such row carries the chord that reaches it, the rule the window rows' ⌥N already follow.
    /// </summary>
    private Dictionary<string, string> CharacterJumpChords()
    {
        var chords = new Dictionary<string, string>(StringComparer.Ordinal);
        var cycle = CommandCatalog.CharacterCycle(BuildCharacterRefs());
        for (var i = 0; i < cycle.Count && i < CommandIds.CharacterJumpDigits; i++)
        {
            chords[cycle[i].SessionKey] = CommandIds.CharacterJumpChord(i + 1);
        }

        return chords;
    }
}
