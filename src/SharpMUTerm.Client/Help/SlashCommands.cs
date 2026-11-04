namespace SharpMUTerm.Client.Help;

/// <summary>One command the command line answers itself rather than sending to the game.</summary>
/// <param name="Name">What starts it (<c>/connect</c>).</param>
/// <param name="Usage">The whole form, arguments included (<c>/connect &lt;host&gt; [port] [tls]</c>).</param>
/// <param name="Summary">What it does, in one line.</param>
/// <param name="Placed">What help puts on the command line when it is chosen: the name, and a space when it takes words.</param>
public sealed record SlashCommand(string Name, string Usage, string Summary, string Placed);

/// <summary>
/// <b>The one list of slash commands.</b> <c>--help</c> prints it, <c>/help</c> and ⌃P ▸ <em>Help</em> list it, and a
/// test holds it to the command line's own dispatch, so a command added in one place and not the others fails.
/// </summary>
public static class SlashCommands
{
    public static IReadOnlyList<SlashCommand> All { get; } =
    [
        new("/help", "/help [words]", "This help: keys, slash commands and short how-tos, searched for the words", "/help "),
        new("/connect", "/connect <world>", "Open a configured world by name", "/connect "),
        new("/connect", "/connect <host> [port] [tls]", "Add a world for a host and connect to it (port 4000 by default)", "/connect "),
        new("/welcome", "/welcome", "Show the first-run welcome again", "/welcome"),
        new("/welcome", "/welcome layout roleplay|combat|suggested", "Choose the starting layout the welcome offers", "/welcome layout "),
        new("/package", "/package", "Open the Packages screen (F10)", "/package"),
        new("/package", "/package browse", "Browse the package index, this game's packages first", "/package browse"),
        new("/package", "/package install <file or https link>", "Read a package and open its review; nothing is written before Enter", "/package install "),
        new("/walk", "/walk <room>", "Speedwalk to a room on this connection's map, one step at a time", "/walk "),
        new("/walk", "/walk stop", "Stop walking", "/walk stop"),
        new("/map", "/map lock|unlock [room]", "Keep walks out of a room, or let them through again", "/map lock "),
        new("/web", "/web <url>", "Open a page in the built-in web view", "/web "),
        new("/graphics", "/graphics", "Say how pictures are drawn in this terminal, and why", "/graphics"),
        new("/triggers", "/triggers", "Say which trigger rules this window's character runs, and why a window did not open", "/triggers"),
    ];

    /// <summary>The distinct names, in table order: what the command line dispatches on.</summary>
    public static IReadOnlyList<string> Names { get; } = All.Select(c => c.Name).Distinct(StringComparer.Ordinal).ToArray();
}
