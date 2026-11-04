using System.Globalization;
using SharpMUTerm.Client.Composer;

namespace SharpMUTerm.Client.Panels;

/// <summary>
/// The heading a docked panel's native view draws over its content: what it is (<see cref="Title"/>, drawn semibold) and
/// whose or where (<see cref="Context"/>, drawn dim beside it) — <c>Compose for Corvid</c>, <c>Here The Grand Plaza · 4</c>,
/// <c>Vitals GMCP Char.Vitals</c>, <c>Map Ossuary · level 0</c>. Read off the model the panel already draws, so the words
/// cannot disagree with what is under them; the cells keep their own headings (a cell spent on a heading is a row of
/// output gone).
/// </summary>
/// <param name="Title">The panel's name.</param>
/// <param name="Context">What it is showing now, or null when there is nothing to add.</param>
public sealed record PanelHeading(string Title, string? Context)
{
    /// <summary>The composer's: whose post it is, or that nobody is connected.</summary>
    public static PanelHeading For(ComposerModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Edit is { } edit)
        {
            return new(edit.Script ? "Script" : "Edit", edit.Name);
        }

        return new("Compose", model.Target.Length == 0 ? "no connection" : $"for {model.Target}");
    }

    /// <summary>Who is here: the room and how many are in it, once the game has said.</summary>
    public static PanelHeading For(WhoHereModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.Known)
        {
            return new("Here", null);
        }

        var count = model.Rows.Count.ToString(CultureInfo.InvariantCulture);
        return new("Here", model.Room is { } room ? $"{room} · {count}" : count);
    }

    /// <summary>The vitals: where the numbers come from, when the model knows.</summary>
    public static PanelHeading For(VitalsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new("Vitals", model.Source);
    }

    /// <summary>The map: the area and level drawn, and that it is not where the character is while it is looking elsewhere.</summary>
    public static PanelHeading For(MapViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var level = $"level {model.Level.ToString(CultureInfo.InvariantCulture)}";
        var where = model.Area is { Length: > 0 } area ? $"{area} · {level}" : level;
        return new("Map", model.Viewing ? $"viewing {where}" : where);
    }
}
