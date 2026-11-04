using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Client.Contributions;

/// <summary>
/// <b>Panels</b> are views of data the client holds for a connection — the game data, the map, a package's own
/// readout. A host decides how to draw one (an overlay in the terminal, a docked pane in the window); the
/// contribution says what it is called and whether there is anything to show yet.
/// </summary>
public sealed class PanelContribution : IContribution
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <inheritdoc />
    public ContributionOwner Owner { get; init; } = ContributionOwner.BuiltIn;

    /// <summary>What the panel is called.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// Whether there is data to show for the connection with this session key (null: none is focused). A host
    /// can offer an empty panel all the same; this lets it say so rather than draw a blank.
    /// </summary>
    public Func<string?, bool> HasData { get; init; } = _ => true;
}

/// <summary>
/// <b>Settings sections</b> are the settings screens, each with the fields it declares. A section with fields
/// is drawn from them (F7 and F8 are; see <see cref="BuiltInSettings"/>); a section with none is still listed,
/// by its title, wherever the screens are listed — the settings search names it and opens it. Sections are read
/// in <see cref="Order"/>, ties in registration order.
/// </summary>
public sealed class SettingsSectionContribution : IContribution
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <inheritdoc />
    public ContributionOwner Owner { get; init; } = ContributionOwner.BuiltIn;

    /// <summary>The section's heading.</summary>
    public required string Title { get; init; }

    /// <summary>Where it sits among the sections; lower first.</summary>
    public int Order { get; init; }

    /// <summary>Its fields, in the order they are shown.</summary>
    public IReadOnlyList<SettingsField> Fields { get; init; } = Array.Empty<SettingsField>();

    /// <summary>The key that opens its screen (<c>F7</c>), or null for a section no key opens.</summary>
    public string? Key { get; init; }

    /// <summary>
    /// What its fields belong to: the client (F7, F8), one world (F5) or one character (F9). A surface drawing a
    /// scoped section first says which world or character it is editing; every field's binding is of this scope.
    /// </summary>
    public SettingScope Scope { get; init; } = SettingScope.App;
}

/// <summary>
/// One field of a settings section: its id within the section, its label and an optional line of help.
/// <para>
/// <see cref="Group"/> is the heading it is drawn under — consecutive fields with the same group share one
/// heading, so a group is a heading and never a screen of its own. <see cref="Binding"/> is where the value
/// lives in the configuration; a field with one can say whether it differs from its default and be reset.
/// <see cref="Keywords"/> are further words the settings search finds the field by.
/// </para>
/// </summary>
public abstract record SettingsField(string Id, string Label, string? Help = null)
{
    /// <summary>The heading the field is drawn under, or null for none.</summary>
    public string? Group { get; init; }

    /// <summary>Where its value lives, or null for a field that only describes.</summary>
    public SettingBinding? Binding { get; init; }

    /// <summary>Further words the search finds it by, space-separated.</summary>
    public string? Keywords { get; init; }
}

/// <summary>A tick box.</summary>
public sealed record BoolField(string Id, string Label, string? Help = null) : SettingsField(Id, Label, Help);

/// <summary>One of a closed set of values.</summary>
public sealed record ChoiceField(string Id, string Label, IReadOnlyList<string> Choices, string? Help = null)
    : SettingsField(Id, Label, Help)
{
    /// <summary>
    /// The choices when they depend on the configuration (a layout row lists the player's own layouts), or null for
    /// the fixed <see cref="Choices"/>.
    /// </summary>
    public Func<AppConfiguration, IReadOnlyList<string>>? ChoicesFrom { get; init; }

    /// <summary>The choices over <paramref name="config"/>.</summary>
    public IReadOnlyList<string> ChoicesIn(AppConfiguration config) => ChoicesFrom?.Invoke(config) ?? Choices;
}

/// <summary>A whole number within bounds.</summary>
public sealed record NumberField(string Id, string Label, int Minimum, int Maximum, string? Help = null)
    : SettingsField(Id, Label, Help);

/// <summary>Free text; <paramref name="Masked"/> for a password.</summary>
public sealed record TextField(string Id, string Label, int? MaxLength = null, bool Masked = false, string? Help = null)
    : SettingsField(Id, Label, Help)
{
    /// <summary>What the text may be and how it is stored (<see cref="TextRules"/>); optional free text by default.</summary>
    public TextRule Rule { get; init; } = TextRule.Optional;

    /// <summary>For <see cref="TextRule.Defaulted"/>: the value an unset field stands for, and is shown and opened as.</summary>
    public string? Fallback { get; init; }
}

/// <summary>
/// The client's theme, picked from a list where each theme is shown as a preview (<see cref="ThemePreview"/>): a pane
/// in a world's colours, a tinted pane, the focused pane, the command line and a tab strip, all in that theme. Its
/// binding holds the theme's name; the choices are <see cref="ThemeChoices.Names"/>.
/// </summary>
public sealed record ThemeField(string Id, string Label, string? Help = null) : SettingsField(Id, Label, Help);

/// <summary>A colour from a named palette (never a free hex: a name survives a theme change, a hex does not).</summary>
public sealed record ColourField(string Id, string Label, IReadOnlyList<string> Names, string? Help = null)
    : SettingsField(Id, Label, Help);

/// <summary>A list of items, each described by <paramref name="Item"/>'s fields.</summary>
public sealed record ListField(string Id, string Label, IReadOnlyList<SettingsField> Item, string? Help = null)
    : SettingsField(Id, Label, Help);

/// <summary>
/// <b>Status cells</b> are the segments of the status row. Every cell reserves its width whether or not it has
/// anything to say, because a cell that costs a column only when it speaks reflows the row (and, through the
/// panes, the size every server is told). When the row is too narrow, cells go in <see cref="DropOrder"/>:
/// the highest first, ties the most recently registered first.
/// </summary>
public sealed class StatusCellContribution : IContribution
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <inheritdoc />
    public ContributionOwner Owner { get; init; } = ContributionOwner.BuiltIn;

    /// <summary>Which goes first when the row runs out of room; higher goes sooner.</summary>
    public int DropOrder { get; init; }

    /// <summary>The cells it holds whether or not it has text.</summary>
    public required int ReservedWidth { get; init; }

    /// <summary>Its text now, or null for blank. Never wider than <see cref="ReservedWidth"/> once drawn.</summary>
    public Func<string?>? Text { get; init; }
}

/// <summary>
/// <b>Overlays</b> are modal surfaces drawn over the workspace (⌃P, ⌃R, ⌃F, the composer). Minimal for now: an
/// id, a title, and the command that opens it, so a surface can list it.
/// </summary>
public sealed class OverlayContribution : IContribution
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <inheritdoc />
    public ContributionOwner Owner { get; init; } = ContributionOwner.BuiltIn;

    /// <summary>What it is called.</summary>
    public required string Title { get; init; }

    /// <summary>The command id that opens it, if one does.</summary>
    public string? OpenedBy { get; init; }
}

/// <summary>
/// <b>Output decorators</b> change a line of output on its way to a pane — link detection and emoji
/// substitution are the client's own. Run in <see cref="Order"/>, lowest first, ties in registration order.
/// Minimal for now; nothing runs them yet.
/// </summary>
public sealed class OutputDecoratorContribution : IContribution
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <inheritdoc />
    public ContributionOwner Owner { get; init; } = ContributionOwner.BuiltIn;

    /// <summary>Where it runs among the decorators; lower first.</summary>
    public int Order { get; init; }

    /// <summary>The change, or null for a descriptor that only names one.</summary>
    public Func<StyledLine, StyledLine>? Apply { get; init; }
}

/// <summary>Settings sections, readable in their declared order.</summary>
public sealed class SettingsSectionList : ContributionList<SettingsSectionContribution>
{
    internal SettingsSectionList()
        : base(ContributionKind.SettingsSection)
    {
    }

    /// <summary>The sections by <see cref="SettingsSectionContribution.Order"/>, ties in registration order.</summary>
    public IReadOnlyList<SettingsSectionContribution> Ordered => SortedBy(s => s.Order);

    /// <inheritdoc />
    protected override void Validate(SettingsSectionContribution contribution)
    {
        var duplicate = contribution.Fields.GroupBy(f => f.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"the section '{contribution.Id}' has two fields with the id '{duplicate.Key}'", nameof(contribution));
        }
    }
}

/// <summary>Status cells, readable in the order they are given up.</summary>
public sealed class StatusCellList : ContributionList<StatusCellContribution>
{
    internal StatusCellList()
        : base(ContributionKind.StatusCell)
    {
    }

    /// <summary>The cells in the order they go when the row is too narrow: highest drop order first, then the newest.</summary>
    public IReadOnlyList<StatusCellContribution> InDropOrder =>
        this.Select((cell, index) => (cell, index))
            .OrderByDescending(p => p.cell.DropOrder)
            .ThenByDescending(p => p.index)
            .Select(p => p.cell)
            .ToList();

    /// <inheritdoc />
    protected override void Validate(StatusCellContribution contribution) =>
        ArgumentOutOfRangeException.ThrowIfNegative(contribution.ReservedWidth);
}

/// <summary>Output decorators, readable in the order they run.</summary>
public sealed class OutputDecoratorList : ContributionList<OutputDecoratorContribution>
{
    internal OutputDecoratorList()
        : base(ContributionKind.OutputDecorator)
    {
    }

    /// <summary>The decorators by <see cref="OutputDecoratorContribution.Order"/>, ties in registration order.</summary>
    public IReadOnlyList<OutputDecoratorContribution> Ordered => SortedBy(d => d.Order);
}
