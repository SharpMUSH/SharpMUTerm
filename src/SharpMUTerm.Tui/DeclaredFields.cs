using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;

namespace SharpMUTerm.Tui;

/// <summary>
/// A declared field (<see cref="BuiltInSettings"/>) as the cell screens' field: the <see cref="ScreenField"/> of the kind
/// its rule names, reading and writing through the field's own binding on <paramref name="target"/>'s world or
/// character. It is how F5's world row and character row are built, so the cell screen and the native settings window
/// edit one object through one binding and refuse the same values.
/// </summary>
internal static class DeclaredFields
{
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

    /// <summary>The cell screen's checkbox for a declared flag, editing <paramref name="target"/>.</summary>
    internal static ScreenToggle Toggle(SettingsField field, SettingTarget target) =>
        field.Binding is SettingBinding<bool> flag
            ? ScreenToggle.Bind(() => flag.Get(target), v => flag.Set(target, v))
            : throw new InvalidOperationException($"'{field.Id}' is not a flag.");
}
