namespace SharpMUTerm.Core.Protocols;

/// <summary>
/// How much of MXP a line is allowed to use, per the specification's line-security model.
/// </summary>
public enum MxpLineMode
{
    /// <summary>Only tags in the open category are honoured. The default at connection start.</summary>
    Open,

    /// <summary>Every tag is honoured.</summary>
    Secure,

    /// <summary>Nothing is parsed. Every character is literal text.</summary>
    Locked,
}
