using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// A declared field (<see cref="BuiltInSettings"/>) as the cell screens' field: the <see cref="ScreenField"/> of the kind
/// its rule names, reading and writing through the field's own binding on <paramref name="target"/>'s world or
/// character. It is how F5's world row and character row are built, so the cell screen and the native settings window
/// edit one object through one binding and refuse the same values.
/// </summary>
internal static class DeclaredFields
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<TriggerSet>, AppConfiguration> Configs = new();

    /// <summary>
    /// A configuration over a list screen's <paramref name="sets"/>, for the item bindings that look a value up in the
    /// sets (the scripts the configuration already names). The list screens are handed the sets rather than the whole
    /// configuration; this holds the very same list, so a binding reads and moves the screen's own sets.
    /// </summary>
    internal static AppConfiguration Over(IReadOnlyList<TriggerSet> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        return Configs.GetValue(sets, s => new AppConfiguration { TriggerSets = s as List<TriggerSet> ?? s.ToList() });
    }

    /// <summary>The field with <paramref name="id"/> in <paramref name="section"/>.</summary>
    internal static SettingsField Find(SettingsSectionContribution section, string id) =>
        section.Fields.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"{section.Id} declares no field '{id}'.");

    /// <summary>The cell screen's field for <paramref name="field"/>, editing <paramref name="target"/>.</summary>
    internal static ScreenField Screen(SettingsField field, SettingTarget target)
    {
        ArgumentNullException.ThrowIfNull(field);
        switch (field)
        {
            case TextField text when field.Binding is SettingBinding<string?> binding:
            {
                string? Get() => binding.Get(target);
                void Set(string? value) => binding.Set(target, value);
                return text.Rule switch
                {
                    TextRule.Name => ScreenField.Name(field.Label, () => Get() ?? string.Empty, v => Set(v)),
                    TextRule.Required => ScreenField.Text(field.Label, () => Get() ?? string.Empty, v => Set(v)),
                    TextRule.Pattern => ScreenField.OptionalPattern(field.Label, Get, Set),
                    TextRule.Secret => ScreenField.Password(field.Label, Get, Set),
                    TextRule.Defaulted => ScreenField.Defaulted(field.Label, Get, Set, text.Fallback ?? string.Empty),
                    TextRule.Image => ScreenField.Optional(field.Label, Get, Set) with { Validate = v => TextRules.Check(text, v) },
                    _ => ScreenField.Optional(field.Label, Get, Set),
                };
            }

            case NumberField number when field.Binding is SettingBinding<int> count:
                return ScreenField.Integer(
                    field.Label, () => count.Get(target), v => count.Set(target, v), number.Minimum, number.Maximum);

            case ChoiceField choice when field.Binding is SettingBinding<string> named:
                return ScreenField.Choice(
                    field.Label, () => named.Get(target), v => named.Set(target, v), choice.ChoicesIn(target.Config));

            case ColourField colour when field.Binding is SettingBinding<string> named:
                return ScreenField.Choice(field.Label, () => named.Get(target), v => named.Set(target, v), colour.Names);

            default:
                throw new InvalidOperationException($"No cell field draws {field.GetType().Name} '{field.Id}'.");
        }
    }

    /// <summary>
    /// The cell screen's field for one of a list section's item fields (<see cref="SettingScope.Item"/>), editing
    /// <paramref name="target"/>'s item: the same factory each list screen used to call by hand, so a declared field
    /// refuses and stores exactly what that row always did. Suggestions come from the field's own
    /// <see cref="TextField.Suggest"/> over <paramref name="context"/>.
    /// </summary>
    /// <param name="owner">The row's <c>set</c> field, which moves the item and so needs the screen's own sets.</param>
    /// <param name="keys">For a key field: the host whose chords it may take.</param>
    /// <param name="taken">For a key field: names the binding already holding a chord.</param>
    internal static ScreenField Item(
        SettingsField field,
        SettingTarget target,
        SettingsContext context,
        Func<ScreenField>? owner = null,
        HostKeys? keys = null,
        Func<string, string?>? taken = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(context);
        switch (field)
        {
            case ChoiceField { MovesItem: true } when owner is not null:
                return owner() with { Label = field.Label };

            case TextField text when field.Binding is SettingBinding<string?> binding:
            {
                string? Get() => binding.Get(target);
                void Set(string? value) => binding.Set(target, value);
                var known = text.Suggest?.Invoke(target, context);
                return text.Rule switch
                {
                    TextRule.Name => ScreenField.Name(field.Label, () => Get() ?? string.Empty, v => Set(v)),
                    TextRule.Required => ScreenField.Text(field.Label, () => Get() ?? string.Empty, v => Set(v), known),
                    TextRule.TriggerPattern => ScreenField.TriggerPattern(
                        field.Label,
                        () => Get() ?? string.Empty,
                        v => Set(v),
                        () => target.Item is Trigger trigger ? trigger.MatchType : TriggerMatchType.Regex),
                    TextRule.RequiredPattern => ScreenField.Pattern(field.Label, () => Get() ?? string.Empty, v => Set(v)),
                    TextRule.Template => ScreenField.Template(field.Label, Get, Set, known),
                    TextRule.Lines => ScreenField.Lines(field.Label, () => Get() ?? string.Empty, v => Set(v)),
                    TextRule.Window => ScreenField.WindowName(
                        field.Label, () => Get() ?? string.Empty, v => Set(v), known ?? Array.Empty<string>()),
                    _ => Screen(field, target),
                };
            }

            case DecimalField number when field.Binding is SettingBinding<double> value:
                return ScreenField.Number(
                    field.Label, () => value.Get(target), v => value.Set(target, v), number.Minimum, number.Maximum);

            case HighlightField when field.Binding is SettingBinding<TerminalColor?> colour:
                return ScreenField.Colour(field.Label, () => colour.Get(target), v => colour.Set(target, v));

            case FlagsField flags when field.Binding is SettingBinding<string> words:
                return new ScreenField(
                    field.Label,
                    () => words.Get(target),
                    value => FlagWords.Unknown(value, flags.Names) is { } bad ? $"{field.Label} has no '{bad}'" : null,
                    value => words.Set(target, value));

            case KeyField when field.Binding is SettingBinding<string> chord && keys is not null:
                return ScreenField.Key(keys, field.Label, () => chord.Get(target), v => chord.Set(target, v), taken ?? (_ => null));

            default:
                return Screen(field, target);
        }
    }

    /// <summary>The cell screen's checkbox for a declared flag, editing <paramref name="target"/>.</summary>
    internal static ScreenToggle Toggle(SettingsField field, SettingTarget target) =>
        field.Binding is SettingBinding<bool> flag
            ? ScreenToggle.Bind(() => flag.Get(target), v => flag.Set(target, v))
            : throw new InvalidOperationException($"'{field.Id}' is not a flag.");
}
