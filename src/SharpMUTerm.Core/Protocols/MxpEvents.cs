using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Protocols;

/// <summary>A server's entity changed: <c>&lt;!ENTITY&gt;</c>, <c>&lt;VAR&gt;</c> or an element's <c>FLAG="set x"</c>. A null value is a deletion.</summary>
public sealed record MxpEntityChange(string Name, string? Value);

/// <summary>The text of an element whose <c>FLAG</c> says what it is: <c>RoomName</c>, <c>RoomDesc</c>, <c>RoomExit</c>, <c>RoomNum</c>…</summary>
public sealed record MxpFlagText(string Flag, string Text);

/// <summary>A <c>&lt;GAUGE&gt;</c> or <c>&lt;STAT&gt;</c>: show entity <see cref="Entity"/> (out of <see cref="Max"/>) on the status row.</summary>
public sealed record MxpGauge(string Entity, string? Max, string? Caption, bool IsStat);

/// <summary>Text a <c>&lt;DEST&gt;</c>, a redirecting <c>&lt;FRAME&gt;</c> or a Pueblo pane sent to a named window instead of the main one.</summary>
public sealed record MxpRedirect(string Frame, StyledLine Line);
