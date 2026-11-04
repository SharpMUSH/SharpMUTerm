using System.Globalization;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Settings;

/// <summary>
/// What a declared setting belongs to: the whole client, one world, or one character. A world's fields and a
/// character's fields are declared once, like the client's own, and edited for whichever world or character a
/// surface has chosen (<see cref="SettingTarget"/>).
/// </summary>
public enum SettingScope
{
    /// <summary>The whole client: a property of <see cref="AppConfiguration"/>.</summary>
    App,

    /// <summary>One world: a property of a <see cref="WorldDefinition"/>.</summary>
    World,

    /// <summary>One character: a property of a <see cref="CharacterDefinition"/>.</summary>
    Character,
}

/// <summary>
/// Where a binding reads and writes: the configuration, and — for a world's or a character's field — the world
/// or character being edited. An <see cref="AppConfiguration"/> converts to one with neither, so every surface
/// that only ever edited app-wide settings passes its configuration exactly as before.
/// </summary>
/// <param name="Config">The live configuration.</param>
/// <param name="World">The world a world-scoped field edits, or null.</param>
/// <param name="Character">The character a character-scoped field edits, or null.</param>
public readonly record struct SettingTarget(
    AppConfiguration Config, WorldDefinition? World = null, CharacterDefinition? Character = null)
{
    /// <summary>An app-wide target over <paramref name="config"/>.</summary>
    public static implicit operator SettingTarget(AppConfiguration config) => new(config);

    /// <summary>Whether this target names what a field of <paramref name="scope"/> edits.</summary>
    public bool Reaches(SettingScope scope) => scope switch
    {
        SettingScope.World => World is not null,
        SettingScope.Character => Character is not null,
        _ => true,
    };
}

/// <summary>
/// Where a declared setting lives in the configuration: how to read it, how to write it, and what it is when nobody
/// has touched it. The untyped half is what every surface needs to say <c>● changed</c> and to reset a value;
/// <see cref="SettingBinding{T}"/> is what a renderer reads the value through.
/// <para>
/// <b>The default is never written down.</b> It is whatever the binding reads off a freshly constructed
/// <see cref="AppConfiguration"/> — or, for a world's or a character's field, a fresh <see cref="WorldDefinition"/> or
/// <see cref="CharacterDefinition"/> — so the configuration types stay the one place a default is decided and a
/// declaration cannot disagree with them.
/// </para>
/// <para>
/// <b>A scoped binding handed a target that does not name its owner</b> (the settings search reading a world's field
/// with no world chosen) reads as <c>per world</c> / <c>per character</c>, is at its default and resets nothing,
/// rather than throwing: the search lists fields from every section and must not need a world to do it. Reading or
/// writing the value itself without one does throw.
/// </para>
/// </summary>
public abstract class SettingBinding
{
    /// <summary>What the setting belongs to.</summary>
    public abstract SettingScope Scope { get; }

    /// <summary>
    /// Whether the value has a default worth going back to. An identity — a world's name and host, a character's
    /// name and password — does not: "reset" would turn one world into a blank template, so such a field never shows
    /// <c>● changed</c> and offers no reset.
    /// </summary>
    public bool Resettable { get; protected init; } = true;

    /// <summary>Whether the value in <paramref name="target"/> is its default.</summary>
    public abstract bool IsDefault(SettingTarget target);

    /// <summary>Puts the default back into <paramref name="target"/>.</summary>
    public abstract void Reset(SettingTarget target);

    /// <summary>The value as text, the way a search result or a summary shows it.</summary>
    public abstract string Text(SettingTarget target);

    /// <summary>The default as text.</summary>
    public abstract string DefaultText { get; }
}

/// <summary>A setting of type <typeparamref name="T"/>.</summary>
/// <typeparam name="T">What the configuration holds.</typeparam>
public sealed class SettingBinding<T> : SettingBinding
{
    private readonly SettingScope _scope;
    private readonly Func<SettingTarget, T> _get;
    private readonly Action<SettingTarget, T> _set;
    private readonly Func<T, string>? _format;
    private readonly Func<T, string>? _describe;
    private readonly Lazy<string> _defaultText;

    /// <param name="get">Reads the value. It must not change the configuration.</param>
    /// <param name="set">Writes the value.</param>
    /// <param name="format">
    /// The value as text, which is also what two values are compared by. Needed for a value with no useful
    /// <c>ToString</c> — a list compares by what it lists, not by reference.
    /// </param>
    /// <param name="describe">
    /// The value as a reader is shown it in a summary, when that is shorter than <paramref name="format"/> —
    /// a list of meters reads as their labels.
    /// </param>
    public SettingBinding(
        Func<AppConfiguration, T> get,
        Action<AppConfiguration, T> set,
        Func<T, string>? format = null,
        Func<T, string>? describe = null)
        : this(
            SettingScope.App,
            OnConfig(get),
            OnConfig(set),
            format,
            describe,
            resettable: true)
    {
    }

    private SettingBinding(
        SettingScope scope,
        Func<SettingTarget, T> get,
        Action<SettingTarget, T> set,
        Func<T, string>? format,
        Func<T, string>? describe,
        bool resettable)
    {
        _scope = scope;
        _get = get;
        _set = set;
        _format = format;
        _describe = describe;
        Resettable = resettable;
        _defaultText = new Lazy<string>(() => Format(Default));
    }

    /// <summary>A field of one world: read and written on the world a surface has chosen.</summary>
    /// <param name="get">Reads the value; the configuration is there for what a world's value is looked up in (its layouts).</param>
    /// <param name="set">Writes the value.</param>
    /// <param name="format">As for the app-wide constructor.</param>
    /// <param name="resettable">False for an identity (<see cref="SettingBinding.Resettable"/>).</param>
    public static SettingBinding<T> ForWorld(
        Func<AppConfiguration, WorldDefinition, T> get,
        Action<AppConfiguration, WorldDefinition, T> set,
        Func<T, string>? format = null,
        bool resettable = true)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        return new SettingBinding<T>(
            SettingScope.World, t => get(t.Config, t.World!), (t, v) => set(t.Config, t.World!, v), format, null, resettable);
    }

    /// <summary>A field of one character: read and written on the character a surface has chosen.</summary>
    /// <param name="get">Reads the value.</param>
    /// <param name="set">Writes the value.</param>
    /// <param name="format">As for the app-wide constructor.</param>
    /// <param name="resettable">False for an identity (<see cref="SettingBinding.Resettable"/>).</param>
    public static SettingBinding<T> ForCharacter(
        Func<AppConfiguration, CharacterDefinition, T> get,
        Action<AppConfiguration, CharacterDefinition, T> set,
        Func<T, string>? format = null,
        bool resettable = true)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        return new SettingBinding<T>(
            SettingScope.Character,
            t => get(t.Config, t.Character!),
            (t, v) => set(t.Config, t.Character!, v),
            format,
            null,
            resettable);
    }

    /// <inheritdoc />
    public override SettingScope Scope => _scope;

    /// <summary>The value in <paramref name="target"/>.</summary>
    public T Get(SettingTarget target) => _get(Checked(target));

    /// <summary>Writes <paramref name="value"/> into <paramref name="target"/>.</summary>
    public void Set(SettingTarget target, T value) => _set(Checked(target), value);

    /// <summary>
    /// The default, read off a fresh configuration (and a fresh world and character) each time — so a default
    /// that is a list is never one instance shared between every configuration that is reset to it.
    /// </summary>
    public T Default => _get(new SettingTarget(new AppConfiguration(), new WorldDefinition(), new CharacterDefinition()));

    /// <summary>A value as text.</summary>
    public string Format(T value) =>
        _format?.Invoke(value) ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

    /// <inheritdoc />
    public override string DefaultText => _defaultText.Value;

    /// <inheritdoc />
    public override bool IsDefault(SettingTarget target) =>
        !Resettable
        || !target.Reaches(_scope)
        || string.Equals(Format(Get(target)), DefaultText, StringComparison.Ordinal);

    /// <inheritdoc />
    public override void Reset(SettingTarget target)
    {
        if (Resettable && target.Reaches(_scope))
        {
            Set(target, Default);
        }
    }

    /// <inheritdoc />
    public override string Text(SettingTarget target)
    {
        if (!target.Reaches(_scope))
        {
            return _scope == SettingScope.World ? "per world" : "per character";
        }

        var value = Get(target);
        return value is bool flag ? (flag ? "on" : "off") : _describe?.Invoke(value) ?? Format(value);
    }

    private SettingTarget Checked(SettingTarget target)
    {
        if (target.Reaches(_scope))
        {
            return target;
        }

        var noun = _scope.ToString().ToLowerInvariant();
        throw new InvalidOperationException($"A {noun} setting needs a {noun} to read or write.");
    }

    private static Func<SettingTarget, T> OnConfig(Func<AppConfiguration, T> get)
    {
        ArgumentNullException.ThrowIfNull(get);
        return t => get(t.Config);
    }

    private static Action<SettingTarget, T> OnConfig(Action<AppConfiguration, T> set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return (t, v) => set(t.Config, v);
    }
}
