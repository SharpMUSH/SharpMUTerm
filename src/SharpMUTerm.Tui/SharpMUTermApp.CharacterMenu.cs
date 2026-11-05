using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Core.Commands;

namespace SharpMUTerm.Tui;

/// <summary>
/// The menu the window's character pill opens (<see cref="HeaderModel.CharacterMenuTarget"/>). The pill used to go to the
/// character in front, which is where the reader already was; it is now the switcher a desktop app keeps beside its
/// search box: every configured character, grouped by world in the sidebar's order, the one in front checked and its
/// cycle neighbours carrying ⌥J/⌥K; then connect or disconnect for the one in front; then Worlds &amp; Characters and Add
/// a world.
/// <para>
/// <b>Every entry is a ⌃P command</b> (<c>char:&lt;key&gt;</c>, <c>world:disconnect</c>/<c>world:reconnect</c>,
/// <c>screen:worlds</c>, <c>term:welcome</c>), offered only when the registry resolves it, so the menu offers nothing the
/// keyboard lacks; that is also the terminal's way to the same doors. It rides the sidebar's row-menu channel
/// (<see cref="ChromeOverlay.RowMenu"/>), so the answer is checked by <see cref="RunSidebarMenuEntry"/> against what
/// was offered, and a command the menu did not carry is refused.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The character pill's menu. Remembered as offered, so the answer can be checked. Internal for tests.</summary>
    internal MenuModel HeaderCharacterMenu()
    {
        var model = BuildSidebar();
        var entries = new List<MenuEntry>();
        bool? frontConnected = null;
        foreach (var world in model.Worlds)
        {
            foreach (var character in world.Characters)
            {
                if (character.Active)
                {
                    frontConnected = character.Connected;
                }

                var name = CharacterNames.Display(world.Name, character.Name);
                var label = character.Connected ? name : $"{name}  (offline)";
                var command = CommandIds.Character(character.SessionKey);
                if (_contributions.Commands.Resolve(command) is not null)
                {
                    entries.Add(new MenuEntry(Snippet(label), character.Chord is "⌥J" or "⌥K" ? character.Chord : null, command,
                        Checked: character.Active, Enabled: !character.Active));
                }
            }
        }

        if (frontConnected is { } connected)
        {
            Separate(entries);
            Offer(entries, connected ? "Disconnect" : "Connect", connected ? "⌥D" : "⌥R",
                connected ? "world:disconnect" : "world:reconnect");
        }

        Separate(entries);
        Offer(entries, "Worlds & characters…", "F5", ScreenCommandPrefix + "worlds");
        Offer(entries, "Add a world…", null, SidebarModel.AddGameTarget);

        var menu = new MenuModel(entries);
        _sidebarMenuOffered = (HeaderModel.CharacterMenuTarget, menu);
        return menu;

        static void Separate(List<MenuEntry> list)
        {
            if (list.Count > 0 && !list[^1].IsSeparator)
            {
                list.Add(MenuEntry.Separator);
            }
        }
    }
}
