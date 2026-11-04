using SharpMUTerm.Core.Commands;

namespace SharpMUTerm.Client.Panels;

/// <summary>One binding on the quick bar, as every view of it draws it.</summary>
/// <param name="Key">The key as printed: <c>F1</c>, <c>⌃F2</c>.</param>
/// <param name="Descriptor">The descriptor the key fires (<c>Ctrl+F2</c>), which the client runs on a click.</param>
/// <param name="Title">The binding's name, on one line.</param>
/// <param name="Fires">Whether pressing the key reaches the binding on this host; one that does not is still
/// clickable and drawn dim.</param>
public sealed record QuickBarItem(string Key, string Descriptor, string Title, bool Fires);

/// <summary>
/// The quick bar's content for one character: its function-key bindings in the order every view lists them, plain
/// keys before modified ones and then by number. The terminal draws them as a row of cells (<c>QuickBarRenderer</c>);
/// the window draws native buttons over that row from the same instance, so an item index names the same binding
/// in both. Nothing here marks a binding as dangerous: the configuration says no such thing, so no view may.
/// </summary>
public sealed record QuickBarModel(IReadOnlyList<QuickBarItem> Items)
{
    /// <summary>The key that opens the bindings, as the hint prints it.</summary>
    public const string EditKey = "F4";

    /// <summary>The hint naming where the keys are edited.</summary>
    public const string EditHint = EditKey + " edits keys";

    /// <summary>The command a click on the hint dispatches: the F4 screen, through the registry.</summary>
    public const string EditCommand = CommandIds.ScreenPrefix + "keypad";

    /// <summary>What every view says when there are no bindings to show.</summary>
    public const string EmptyText = "No function keys bound";

    /// <summary>
    /// The window's button for <see cref="EditCommand"/>, where it has the room for a button rather than the cells' link:
    /// the same door, with <see cref="EditKey"/> in its tooltip.
    /// </summary>
    public const string BindLabel = "+ Bind a key";

    /// <summary>No bindings.</summary>
    public static QuickBarModel Empty { get; } = new([]);

    /// <summary>Value equality over the items, which a record's list member does not give.</summary>
    public bool Equals(QuickBarModel? other) => other is not null && Items.SequenceEqual(other.Items);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
