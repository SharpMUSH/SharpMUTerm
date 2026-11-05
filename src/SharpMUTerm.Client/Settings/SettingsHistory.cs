using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Settings;

/// <summary>What an undo or a redo did, in the words both hosts show: <c>Undid port.</c>, or why nothing was done.</summary>
/// <param name="Done">Whether a change was undone or redone; false when there was nothing to.</param>
/// <param name="Note">The sentence the host shows for it.</param>
/// <param name="Tag">What the edit was recorded with (the cell screens' deletion), or null.</param>
public sealed record SettingsUndoResult(bool Done, string Note, object? Tag = null);

/// <summary>
/// Undo and redo of committed settings edits, for the run, one history per settings section (F7's edits are undone on
/// F7). The save funnel (the Tui's <c>ScreenEdits</c>, which both hosts' settings commit through) takes a
/// <see cref="Capture"/> before a change and <see cref="Record"/>s it after the change is saved; nothing else writes here.
/// <para>
/// <b>An edit is a difference, not a snapshot.</b> Each step keeps the configuration as JSON before and after, and an
/// undo writes the <em>before</em> values back only at the paths that step changed, onto the configuration as it is now —
/// so undoing an F7 edit leaves an F5 edit made since alone. A path that no longer exists (a list since resized) is
/// written back at the deepest part that does.
/// </para>
/// <para>
/// <b>The live objects are kept.</b> The result is merged into the live configuration rather than replacing it: a world a
/// session holds stays that object, a list element that did not change stays that instance, and a list that changed is
/// <em>replaced</em> (never edited in place), the rule every other writer follows because the window reads lists from
/// its own thread. The JSON includes the one ignored scalar (a character's password), so a password edit undoes too;
/// it is in memory only, as the password already is.
/// </para>
/// </summary>
public sealed class SettingsHistory
{
    /// <summary>How many edits each section keeps to undo.</summary>
    public const int Depth = 50;

    /// <summary>Paths that are not settings and that saving writes (the workspace, the tour flag, the schema version).</summary>
    private static readonly HashSet<string> NotSettings = new(StringComparer.Ordinal) { "lastSession", "keyTourPending", "version" };

    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly AppConfiguration _config;
    private readonly Func<string, string?> _title;
    private readonly Dictionary<string, (List<Step> Undo, List<Step> Redo)> _stacks = new(StringComparer.Ordinal);

    /// <param name="config">The live configuration the edits were made to.</param>
    /// <param name="title">A section's title by id, for the sentences (<c>Nothing to undo in Text &amp; display.</c>).</param>
    public SettingsHistory(AppConfiguration config, Func<string, string?>? title = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _title = title ?? (_ => null);
    }

    /// <summary>The configuration now, to hand back to <see cref="Record"/> once the change is made and saved.</summary>
    public JsonNode Capture() => Snapshot(_config);

    /// <summary>
    /// Records the change made since <paramref name="before"/> as one step of <paramref name="section"/>'s history, and
    /// forgets what that section could redo. A change that changed nothing is not recorded. False when nothing was.
    /// </summary>
    /// <param name="section">The section the edit was made on.</param>
    /// <param name="before">What <see cref="Capture"/> returned before the change.</param>
    /// <param name="what">What was changed, in the section's own words (a field's label), or null.</param>
    /// <param name="tag">Anything the caller wants back when the step is undone or redone.</param>
    public bool Record(string section, JsonNode before, string? what = null, object? tag = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(before);
        var after = Capture();
        if (!Differences(before, after).Any())
        {
            return false;
        }

        var (undo, redo) = Stacks(section);
        undo.Add(new Step(before, after, what, tag));
        if (undo.Count > Depth)
        {
            undo.RemoveAt(0);
        }

        redo.Clear();
        return true;
    }

    /// <summary>Whether <paramref name="section"/> has an edit to undo.</summary>
    public bool CanUndo(string section) => _stacks.TryGetValue(section, out var s) && s.Undo.Count > 0;

    /// <summary>Whether <paramref name="section"/> has an undone edit to redo.</summary>
    public bool CanRedo(string section) => _stacks.TryGetValue(section, out var s) && s.Redo.Count > 0;

    /// <summary>
    /// Puts <paramref name="section"/>'s last edit back, on the live configuration. The caller saves it (through the same
    /// funnel as any change); this only writes the values.
    /// </summary>
    public SettingsUndoResult Undo(string section) => Move(section, redo: false);

    /// <summary>Makes <paramref name="section"/>'s last undone edit again.</summary>
    public SettingsUndoResult Redo(string section) => Move(section, redo: true);

    private SettingsUndoResult Move(string section, bool redo)
    {
        ArgumentNullException.ThrowIfNull(section);
        var (undo, redoStack) = Stacks(section);
        var from = redo ? redoStack : undo;
        var to = redo ? undo : redoStack;
        if (from.Count == 0)
        {
            var title = _title(section);
            var where = string.IsNullOrEmpty(title) ? string.Empty : $" in {title}";
            return new SettingsUndoResult(false, redo ? $"Nothing to redo{where}." : $"Nothing to undo{where}.");
        }

        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        Apply(step.Before, step.After, redo ? step.After : step.Before);
        to.Add(step);
        var verb = redo ? "Redid" : "Undid";
        return new SettingsUndoResult(true, string.IsNullOrWhiteSpace(step.What) ? $"{verb} the last change." : $"{verb} {step.What}.", step.Tag);
    }

    private (List<Step> Undo, List<Step> Redo) Stacks(string section)
    {
        if (!_stacks.TryGetValue(section, out var stacks))
        {
            stacks = ([], []);
            _stacks[section] = stacks;
        }

        return stacks;
    }

    /// <summary>Writes <paramref name="source"/>'s values at every path <paramref name="a"/> and <paramref name="b"/> differ at.</summary>
    private void Apply(JsonNode a, JsonNode b, JsonNode source)
    {
        var current = Snapshot(_config);
        foreach (var path in Differences(a, b).ToList())
        {
            Write(current, source, path);
        }

        if (current.Deserialize<AppConfiguration>(Options) is { } target)
        {
            Merge(_config, target);
        }
    }

    /// <summary>Every path at which two documents differ: down through objects, and arrays of one length, to the leaves.</summary>
    private static IEnumerable<object[]> Differences(JsonNode? a, JsonNode? b, object[]? path = null)
    {
        path ??= [];
        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var key in oa.Select(p => p.Key).Union(ob.Select(p => p.Key)).ToList())
            {
                if (path.Length == 0 && NotSettings.Contains(key))
                {
                    continue;
                }

                foreach (var d in Differences(oa[key], ob[key], [.. path, key]))
                {
                    yield return d;
                }
            }
        }
        else if (a is JsonArray xa && b is JsonArray xb && xa.Count == xb.Count)
        {
            for (var i = 0; i < xa.Count; i++)
            {
                foreach (var d in Differences(xa[i], xb[i], [.. path, i]))
                {
                    yield return d;
                }
            }
        }
        else if (!JsonNode.DeepEquals(a, b))
        {
            yield return path;
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/>'s value at <paramref name="path"/> into <paramref name="target"/>; where the target
    /// no longer has the path, the deepest part it has is replaced whole.
    /// </summary>
    private static void Write(JsonNode target, JsonNode source, object[] path)
    {
        JsonNode? t = target;
        JsonNode? s = source;
        JsonNode? tParent = null;
        for (var i = 0; i < path.Length; i++)
        {
            var last = i == path.Length - 1;
            var tChild = Child(t, path[i], out var tHas);
            var sChild = Child(s, path[i], out var sHas);
            if (!tHas && i > 0 && (!last || t is JsonArray))
            {
                // The target no longer reaches this far (an item since removed): the level above is replaced whole.
                Put(tParent, path[i - 1], s?.DeepClone(), remove: false);
                return;
            }

            if (last || !tHas || !sHas || tChild is null || sChild is null)
            {
                Put(t, path[i], sHas ? sChild?.DeepClone() : null, remove: !sHas);
                return;
            }

            tParent = t;
            t = tChild;
            s = sChild;
        }
    }

    private static JsonNode? Child(JsonNode? node, object segment, out bool has)
    {
        switch (node, segment)
        {
            case (JsonObject o, string key):
                has = o.TryGetPropertyValue(key, out var value);
                return value;
            case (JsonArray a, int index) when index < a.Count:
                has = true;
                return a[index];
            default:
                has = false;
                return null;
        }
    }

    private static void Put(JsonNode? parent, object segment, JsonNode? value, bool remove)
    {
        switch (parent, segment)
        {
            case (JsonObject o, string key):
                if (remove)
                {
                    o.Remove(key);
                }
                else
                {
                    o[key] = value;
                }

                break;
            case (JsonArray a, int index) when index < a.Count && !remove:
                a[index] = value;
                break;
        }
    }

    // ── the merge into the live objects ──────────────────────────────────────────────────────────

    /// <summary>Writes <paramref name="target"/>'s values into <paramref name="live"/>, keeping every live object it can.</summary>
    private static void Merge(object live, object target)
    {
        foreach (var property in Properties(live.GetType()))
        {
            var was = property.GetValue(live);
            var now = property.GetValue(target);
            if (Same(was, now, property.PropertyType))
            {
                continue;
            }

            if (was is IList wasList && now is IList nowList && property.CanWrite && !property.PropertyType.IsArray)
            {
                property.SetValue(live, MergeList(wasList, nowList));
            }
            else if (was is not null && now is not null && was.GetType() == now.GetType() && IsComposite(was.GetType()))
            {
                Merge(was, now);
            }
            else if (property.CanWrite)
            {
                property.SetValue(live, now);
            }
        }
    }

    /// <summary>
    /// A new list of <paramref name="target"/>'s elements: an element equal to a live one is that live instance, one that
    /// changed in place is merged into the live element at its index, and the rest are new.
    /// </summary>
    private static IList MergeList(IList live, IList target)
    {
        var result = (IList)Activator.CreateInstance(live.GetType())!;
        var element = live.GetType().GetGenericArguments().FirstOrDefault();
        if (element is null || !IsComposite(element))
        {
            foreach (var item in target)
            {
                result.Add(item);
            }

            return result;
        }

        var liveJson = live.Cast<object?>().Select(o => Node(o, element)).ToArray();
        var used = new bool[live.Count];
        var chosen = new object?[target.Count];
        for (var i = 0; i < target.Count; i++)
        {
            var json = Node(target[i], element);
            for (var j = 0; j < live.Count; j++)
            {
                if (!used[j] && JsonNode.DeepEquals(liveJson[j], json))
                {
                    used[j] = true;
                    chosen[i] = live[j];
                    break;
                }
            }
        }

        for (var i = 0; i < target.Count; i++)
        {
            if (chosen[i] is not null)
            {
                continue;
            }

            if (i < live.Count && !used[i] && live[i] is { } same && target[i] is { } with && same.GetType() == with.GetType())
            {
                used[i] = true;
                Merge(same, with);
                chosen[i] = same;
            }
            else
            {
                chosen[i] = target[i];
            }
        }

        foreach (var item in chosen)
        {
            result.Add(item);
        }

        return result;
    }

    private static bool Same(object? a, object? b, Type type)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return type.IsValueType || a is string ? Equals(a, b) : JsonNode.DeepEquals(Node(a, type), Node(b, type));
    }

    /// <summary>A class merged property by property: not a string, a collection, or a record (a record may be shared).</summary>
    private static bool IsComposite(Type type) =>
        type.IsClass && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type)
        && type.GetMethod("<Clone>$") is null;

    /// <summary>The properties the configuration's JSON carries, plus the ignored scalars (a password).</summary>
    private static IEnumerable<PropertyInfo> Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null || IsScalar(p.PropertyType));

    private static bool IsScalar(Type type) => type.IsValueType || type == typeof(string);

    private static JsonNode Snapshot(AppConfiguration config) => JsonSerializer.SerializeToNode(config, Options)!;

    private static JsonNode? Node(object? value, Type type) => JsonSerializer.SerializeToNode(value, type, Options);

    /// <summary>
    /// The store's options, with nulls written (so a document reads back exactly) and the ignored scalars included.
    /// </summary>
    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(IncludeIgnoredScalars);
        return new JsonSerializerOptions(ConfigurationStore.SerializerOptions)
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            TypeInfoResolver = resolver,
        };
    }

    private static void IncludeIgnoredScalars(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in info.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is null || !IsScalar(property.PropertyType)
                || !property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var name = info.Options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
            if (info.Properties.FirstOrDefault(p => p.Name == name) is { } ignored)
            {
                info.Properties.Remove(ignored);
            }

            var added = info.CreateJsonPropertyInfo(property.PropertyType, name);
            added.Get = property.GetValue;
            added.Set = property.SetValue;
            info.Properties.Add(added);
        }
    }

    private sealed record Step(JsonNode Before, JsonNode After, string? What, object? Tag);
}
