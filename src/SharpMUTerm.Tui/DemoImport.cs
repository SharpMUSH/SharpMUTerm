namespace SharpMUTerm.Tui;

/// <summary>
/// A MUSHclient world file the demo's <c>import-review</c> view reads, written for this client (no third-party file):
/// one world with a player and a password, rules that import, and rules that cannot — a script function, a send to a
/// script, a time-of-day timer, a command-line macro and a plugin — so the review shows each of its sections.
/// </summary>
internal static class DemoImport
{
    internal const string MushclientName = "Northwind.mcl";

    internal const string Mushclient = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE muclient>
        <muclient>
        <world name="Northwind" site="northwind.example.org" port="4201" player="Wren" password="not-a-real-one" connect_method="1" />
        <triggers>
          <trigger enabled="y" match="* pages: *" sequence="100"><send>page %1=Away, back soon.</send></trigger>
          <trigger enabled="y" match="^You are hungry\.$" regexp="y" sequence="100"><send>eat bread</send></trigger>
          <trigger enabled="y" match="*has arrived.*" sequence="100" omit_from_output="y"></trigger>
          <trigger enabled="y" match="* says, &quot;*&quot;" sequence="100" script="OnSay"><send>emote nods.</send></trigger>
          <trigger enabled="y" match="Your health is *" sequence="100" send_to="12"><send>UpdateGauge("%1")</send></trigger>
        </triggers>
        <aliases>
          <alias match="gg *" enabled="y" sequence="100"><send>get %1
        give %1 to Wren</send></alias>
          <alias match="^ws$" enabled="y" regexp="y" sequence="100"><send>wake
        stand</send></alias>
        </aliases>
        <timers>
          <timer name="idle" enabled="y" minute="5" second="0.00"><send>look</send></timer>
          <timer name="midnight" enabled="y" hour="0" minute="0" second="0.00" at_time="y"><send>say midnight</send></timer>
        </timers>
        <macros>
          <macro name="F2" type="send_now"><send>score</send></macro>
          <macro name="F3" type="replace"><send>say </send></macro>
        </macros>
        <keypad>
          <key name="8"><send>north</send></key>
          <key name="2"><send>south</send></key>
        </keypad>
        <include name="health_bar.xml" plugin="y" />
        </muclient>
        """;
}
