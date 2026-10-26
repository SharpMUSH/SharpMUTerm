using System.Globalization;
using System.Text.Json.Nodes;
using MoonSharp.Interpreter;

namespace SharpMUTerm.Scripting;

/// <summary>
/// Converts between game data (JSON) and Lua values, for <c>gmcp.get</c>, <c>msdp.get</c> and
/// <c>gmcp.send</c>. An object is a table with string keys, an array a table indexed from 1.
/// </summary>
internal static class LuaJson
{
    /// <summary>Deeper than this and a Lua table is refused: a table can contain itself.</summary>
    private const int MaxDepth = 32;

    public static DynValue ToLua(Script script, JsonNode? node) => node switch
    {
        null => DynValue.Nil,
        JsonObject obj => Table(script, obj),
        JsonArray array => Table(script, array),
        JsonValue value => Scalar(value),
        _ => DynValue.Nil,
    };

    /// <summary>A Lua value as JSON, or null for nil. Throws <see cref="ScriptRuntimeException"/> for a function, a cycle or too much nesting.</summary>
    public static JsonNode? FromLua(DynValue value, int depth = 0)
    {
        if (depth > MaxDepth)
        {
            throw new ScriptRuntimeException("gmcp.send: the table is nested too deeply (or contains itself).");
        }

        switch (value.Type)
        {
            case DataType.Nil:
            case DataType.Void:
                return null;
            case DataType.Boolean:
                return JsonValue.Create(value.Boolean);
            case DataType.Number:
                var n = value.Number;
                return n == Math.Floor(n) && Math.Abs(n) < 9e15 ? JsonValue.Create((long)n) : JsonValue.Create(n);
            case DataType.String:
                return JsonValue.Create(value.String);
            case DataType.Table:
                return FromTable(value.Table, depth);
            default:
                throw new ScriptRuntimeException($"gmcp.send: a {value.Type.ToLuaTypeString()} cannot be sent.");
        }
    }

    private static JsonNode FromTable(Table table, int depth)
    {
        var length = table.Length;
        var count = table.Pairs.Count();
        if (length > 0 && length == count)
        {
            var array = new JsonArray();
            for (var i = 1; i <= length; i++)
            {
                array.Add(FromLua(table.Get(i), depth + 1));
            }

            return array;
        }

        var obj = new JsonObject();
        foreach (var pair in table.Pairs)
        {
            var key = pair.Key.Type switch
            {
                DataType.String => pair.Key.String,
                DataType.Number => pair.Key.Number.ToString(CultureInfo.InvariantCulture),
                _ => throw new ScriptRuntimeException("gmcp.send: a table key must be a string or a number."),
            };
            obj[key] = FromLua(pair.Value, depth + 1);
        }

        return obj;
    }

    private static DynValue Table(Script script, JsonObject obj)
    {
        var table = new Table(script);
        foreach (var (key, child) in obj)
        {
            table[key] = ToLua(script, child);
        }

        return DynValue.NewTable(table);
    }

    private static DynValue Table(Script script, JsonArray array)
    {
        var table = new Table(script);
        for (var i = 0; i < array.Count; i++)
        {
            table[i + 1] = ToLua(script, array[i]);
        }

        return DynValue.NewTable(table);
    }

    private static DynValue Scalar(JsonValue value)
    {
        if (value.TryGetValue(out string? text))
        {
            return DynValue.NewString(text);
        }

        if (value.TryGetValue(out bool flag))
        {
            return DynValue.NewBoolean(flag);
        }

        return double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? DynValue.NewNumber(n)
            : DynValue.Nil;
    }
}
