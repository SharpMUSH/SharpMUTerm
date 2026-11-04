using System.Globalization;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Settings;

/// <summary>
/// Where a declared setting lives in the <see cref="AppConfiguration"/>: how to read it, how to write it, and
/// what it is when nobody has touched it. The untyped half is what every surface needs to say
/// <c>● changed</c> and to reset a value; <see cref="SettingBinding{T}"/> is what a renderer reads the value
/// through.
/// <para>
/// <b>The default is never written down.</b> It is whatever the binding reads off a freshly constructed
/// <see cref="AppConfiguration"/>, so the configuration types stay the one place a default is decided and a
/// declaration cannot disagree with them.
/// </para>
/// </summary>
public abstract class SettingBinding
{
    /// <summary>Whether the value in <paramref name="config"/> is its default.</summary>
    public abstract bool IsDefault(AppConfiguration config);

    /// <summary>Puts the default back into <paramref name="config"/>.</summary>
    public abstract void Reset(AppConfiguration config);

    /// <summary>The value as text, the way a search result or a summary shows it.</summary>
    public abstract string Text(AppConfiguration config);

    /// <summary>The default as text.</summary>
    public abstract string DefaultText { get; }
}

/// <summary>A setting of type <typeparamref name="T"/>.</summary>
/// <typeparam name="T">What the configuration holds.</typeparam>
public sealed class SettingBinding<T> : SettingBinding
{
    private readonly Func<AppConfiguration, T> _get;
    private readonly Action<AppConfiguration, T> _set;
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
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        _get = get;
        _set = set;
        _format = format;
        _describe = describe;
        _defaultText = new Lazy<string>(() => Format(Default));
    }

    /// <summary>The value in <paramref name="config"/>.</summary>
    public T Get(AppConfiguration config) => _get(config);

    /// <summary>Writes <paramref name="value"/> into <paramref name="config"/>.</summary>
    public void Set(AppConfiguration config, T value) => _set(config, value);

    /// <summary>
    /// The default, read off a fresh configuration each time — so a default that is a list is never one
    /// instance shared between every configuration that is reset to it.
    /// </summary>
    public T Default => _get(new AppConfiguration());

    /// <summary>A value as text.</summary>
    public string Format(T value) =>
        _format?.Invoke(value) ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

    /// <inheritdoc />
    public override string DefaultText => _defaultText.Value;

    /// <inheritdoc />
    public override bool IsDefault(AppConfiguration config) =>
        string.Equals(Format(Get(config)), DefaultText, StringComparison.Ordinal);

    /// <inheritdoc />
    public override void Reset(AppConfiguration config) => Set(config, Default);

    /// <inheritdoc />
    public override string Text(AppConfiguration config)
    {
        var value = Get(config);
        return value is bool flag ? (flag ? "on" : "off") : _describe?.Invoke(value) ?? Format(value);
    }
}
