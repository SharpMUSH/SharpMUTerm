using SharpMUTerm.Core.Diagnostics;

namespace SharpMUTerm.Client.Modals;

/// <summary>
/// The client's own messages (⌃P ▸ Show client messages): what the client has said this run, oldest first, and the
/// quietest level it is keeping. Both hosts draw this one record: the cells as rows of a modal, the window as a list.
/// </summary>
/// <param name="Level">The quietest level captured, lower case (<c>information</c>, <c>debug</c>, …).</param>
/// <param name="Entries">What was said, oldest first.</param>
public sealed record MessageLogModel(string Level, IReadOnlyList<ClientMessage> Entries)
{
    /// <summary>The surface's title, as both hosts word it.</summary>
    public const string Title = "Client messages";

    /// <summary>What a log with nothing in it says.</summary>
    public const string Empty = "no client messages yet";

    /// <summary>The keys, as the cells' header and the window's footer say them.</summary>
    public const string Keys = "+/- level · Esc close";

    /// <summary>The line naming what is kept.</summary>
    public string Capturing => $"capturing {Level} and louder";

    /// <summary>A severity as a row labels it.</summary>
    public static string Label(MessageSeverity severity) => severity switch
    {
        MessageSeverity.Error => "error",
        MessageSeverity.Warning => "warn",
        _ => "info",
    };

    /// <summary>When a message was said, as a row stamps it (local time).</summary>
    public static string Stamp(ClientMessage message) => message.At.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    public bool Equals(MessageLogModel? other) =>
        other is not null && Level == other.Level && ModelLists.Same(Entries, other.Entries);

    public override int GetHashCode() => HashCode.Combine(Level, ModelLists.Hash(Entries));
}

/// <summary>The client messages open in a host's own controls; <see cref="StepLevel"/> is the cells' <c>+</c>/<c>-</c>.</summary>
public sealed class MessageLogSession : ModalSession<MessageLogModel>
{
    private readonly Action<int> _step;

    public MessageLogSession(MessageLogModel model, bool isDark, Action<int> step, Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(step);
        _step = step;
    }

    public override string Surface => Surfaces.SurfaceCatalog.Messages;

    /// <summary>
    /// Captures one level louder (−1: one more level kept) or quieter (+1); the client republishes the model. A session
    /// that has ended passes nothing on.
    /// </summary>
    public void StepLevel(int delta)
    {
        if (!IsEnded)
        {
            _step(delta);
        }
    }
}
