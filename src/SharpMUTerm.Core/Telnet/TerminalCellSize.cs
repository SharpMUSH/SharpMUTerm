namespace SharpMUTerm.Core.Telnet;

/// <summary>One terminal character cell in physical pixels, not display-independent units.</summary>
public sealed record TerminalCellSize(int Width, int Height)
{
    public bool IsValid => Width is > 0 and <= 65535 && Height is > 0 and <= 65535;
}
