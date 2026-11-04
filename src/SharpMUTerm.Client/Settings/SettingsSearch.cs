using SharpMUTerm.Client.Contributions;

namespace SharpMUTerm.Client.Settings;

/// <summary>
/// One thing the settings search found: a field of a section, or — for a section that declares no fields, or
/// whose title matched where none of its fields did — the section itself. <see cref="Start"/> and
/// <see cref="Length"/> say where in <see cref="Text"/> the query was found, so a host can mark it; both are
/// -1/0 when the match was in text the host does not draw (the help line or the keywords).
/// </summary>
/// <param name="Section">The section it is in.</param>
/// <param name="Field">The field, or null for the section itself.</param>
/// <param name="Text">What a result row reads: the field's label, or the section's title.</param>
/// <param name="Start">Where the query starts in <see cref="Text"/>, or -1.</param>
/// <param name="Length">How long the matched run is.</param>
public sealed record SettingsMatch(
    SettingsSectionContribution Section, SettingsField? Field, string Text, int Start, int Length);

/// <summary>
/// The settings search: a query matched against every declared field's label, group, help line and keywords —
/// a list section's item fields included —
/// and against every section's title — so a screen whose fields are not declared yet is still found by name.
/// Case is ignored and the match is a plain substring, the rule ⌃R and ⌃F already follow. Pure, so the whole
/// contract is testable without a screen.
/// </summary>
public static class SettingsSearch
{
    /// <summary>
    /// Every match for <paramref name="query"/>, in section order and then field order. An empty query matches
    /// nothing: there is no list to narrow, and every field at once is what the screens already are.
    /// </summary>
    public static IReadOnlyList<SettingsMatch> Find(IEnumerable<SettingsSectionContribution> sections, string? query)
    {
        ArgumentNullException.ThrowIfNull(sections);

        var q = (query ?? string.Empty).Trim();
        var matches = new List<SettingsMatch>();
        if (q.Length == 0)
        {
            return matches;
        }

        foreach (var section in sections)
        {
            var found = 0;
            foreach (var field in Searchable(section))
            {
                var at = field.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase);
                if (at >= 0 || Contains(field.Group, q) || Contains(field.Help, q) || Contains(field.Keywords, q))
                {
                    matches.Add(new SettingsMatch(section, field, field.Label, at, at >= 0 ? q.Length : 0));
                    found++;
                }
            }

            var title = section.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase);
            if (found == 0 && title >= 0)
            {
                matches.Add(new SettingsMatch(section, null, section.Title, title, q.Length));
            }
        }

        return matches;
    }

    /// <summary>How many of <paramref name="matches"/> are in each section, by section id.</summary>
    public static IReadOnlyDictionary<string, int> CountBySection(IEnumerable<SettingsMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return matches.GroupBy(m => m.Section.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    }

    /// <summary>
    /// A section's searchable fields: its own, then — for a list section — every field of its items, so <c>route</c> or
    /// <c>expansion</c> finds the screen that edits one.
    /// </summary>
    public static IEnumerable<SettingsField> Searchable(SettingsSectionContribution section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return section.Collection is { } collection ? section.Fields.Concat(collection.Fields)
            : section.Packages is { } packages ? section.Fields.Concat(packages.Fields)
            : section.Fields;
    }

    private static bool Contains(string? text, string query) =>
        text is not null && text.Contains(query, StringComparison.OrdinalIgnoreCase);
}
