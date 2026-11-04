using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Client.Settings;

/// <summary>
/// The list sections — F2 triggers, F3 aliases, F4 key bindings, F6 timers — declared: each a
/// <see cref="SettingsCollection"/> whose item fields are ordinary descriptors over <see cref="SettingScope.Item"/>
/// bindings. The cell screens build their rows from these fields (in their own ⇥ order, by id) and the window draws
/// them as a master-detail editor, so both list the same fields and edit the same objects.
/// </summary>
public static partial class BuiltInSettings
{
    /// <summary>F2's item groups.</summary>
    public const string RuleGroup = "Rule";

    /// <summary>Where a matched line goes and how it looks.</summary>
    public const string DeliveryGroup = "Route & highlight";

    /// <summary>What a matched line makes happen.</summary>
    public const string ActionsGroup = "Actions";

    /// <summary>The rule's switches.</summary>
    public const string BehaviourGroup = "Behaviour";

    /// <summary>
    /// What the route field calls a rule that delivers nowhere of its own (a null <c>SpawnTarget</c>): the line goes
    /// where the other matched rules send it, else to the session's own window. A new rule's route.
    /// </summary>
    public const string NoRoute = "(none)";

    /// <summary>A new trigger's name and pattern — a placeholder rather than blank, because a blank pattern is refused.</summary>
    public const string NewTriggerName = "New Trigger";

    /// <summary>A new trigger's pattern.</summary>
    public const string NewTriggerPattern = "text to match";

    /// <summary>A new alias's name.</summary>
    public const string NewAliasName = "New Alias";

    /// <summary>A new alias's pattern.</summary>
    public const string NewAliasPattern = "^alias$";

    /// <summary>A new alias's expansion.</summary>
    public const string NewAliasSubstitution = "look";

    /// <summary>A new timer's name.</summary>
    public const string NewTimerName = "New Timer";

    /// <summary>A new timer's interval: a minute, long enough that a timer switched on by accident is not a flood.</summary>
    public const double NewTimerIntervalSeconds = 60;

    /// <summary>A new timer's command.</summary>
    public const string NewTimerCommand = "look";

    /// <summary>A new key binding's name.</summary>
    public const string NewBindingName = "New Binding";

    /// <summary>A new key binding's command.</summary>
    public const string NewBindingCommand = "look";

    /// <summary>The shortest interval a timer may have; zero is "off", which is what its check box is for.</summary>
    public const double MinIntervalSeconds = 0.1;

    /// <summary>A day: past this an interval is likelier a typo than a schedule.</summary>
    public const double MaxIntervalSeconds = 86400;

    /// <summary>The words F2's match field offers, in <see cref="TriggerPatterns.Offered"/>'s order.</summary>
    public static IReadOnlyList<string> MatchTypeNames { get; } =
        TriggerPatterns.Offered.Select(TriggerPatterns.Describe).ToArray();

    /// <summary>The words F2's attributes field accepts.</summary>
    public static IReadOnlyList<string> AttributeNames { get; } = FlagWords.Names<TextAttributes>();

    /// <summary>F2: the trigger rules of every set.</summary>
    public static SettingsSectionContribution Triggers { get; } = new()
    {
        Id = "triggers",
        Title = "Triggers & spawn routing",
        Key = "F2",
        Order = 20,
        Scope = SettingScope.Item,
        Collection = new SettingsCollection<Trigger>
        {
            Noun = "trigger",
            Plural = "triggers",
            Items = s => s.Triggers,
            SetItems = (s, v) => s.Triggers = v,
            New = (_, _) => new Trigger { Name = NewTriggerName, Pattern = NewTriggerPattern },
            Copy = t => t.Clone(),
            Name = t => t.Name,
            Rename = (t, v) => t.Name = v,
            Title = t => t.Pattern,
            Detail = t => $"{TriggerPatterns.Describe(t.MatchType)}: {t.Pattern}",
            Fields =
            [
                ItemFlag<Trigger>("enabled", "enabled", RuleGroup, t => t.Enabled, (t, v) => t.Enabled = v,
                    "whether the rule runs", "on off disable", () => NewTrigger()),
                ItemText<Trigger>("name", "name", TextRule.Name, RuleGroup, (_, t) => t.Name, (_, t, v) => t.Name = v ?? t.Name,
                    "what the rule is called in the list", NewTrigger),
                new ChoiceField("match", "match", MatchTypeNames, "how the pattern is read")
                {
                    Group = RuleGroup,
                    Keywords = "contains starts wildcard regex regular expression kind",
                    Binding = SettingBinding<string>.ForItem<Trigger>(
                        (_, t) => TriggerPatterns.Describe(t.MatchType),
                        (_, t, v) => SetMatchType(t, TriggerPatterns.Parse(v) ?? t.MatchType),
                        NewTrigger),
                },
                ItemText<Trigger>("pattern", "match pattern", TextRule.TriggerPattern, RuleGroup,
                    (_, t) => t.Pattern, (_, t, v) => t.Pattern = v ?? t.Pattern,
                    "the text a line is matched against; the live test shows what it matches", NewTrigger,
                    "regex wildcard test preview"),
                new ChoiceField("set", "set", [], "the trigger set the rule lives in")
                {
                    Group = RuleGroup,
                    MovesItem = true,
                    Keywords = "trigger set group",
                    ChoicesFrom = c => c.TriggerSets.Select(s => s.Name).ToArray(),
                    Binding = SettingBinding<string>.ForItem<Trigger>(
                        (c, t) => c.TriggerSets.FirstOrDefault(s => s.Triggers.Contains(t))?.Name ?? string.Empty,
                        (c, t, v) => Triggers!.Collection!.MoveTo(c, t, v),
                        NewTrigger),
                },

                new TextField("route", "route", Help: "the window the line goes to; (none) leaves it where other rules send it")
                {
                    Group = DeliveryGroup,
                    Rule = TextRule.Window,
                    Keywords = "spawn window capture channel tab destination",
                    Suggest = (t, context) => Routes((Trigger)t.Item!, context.Windows),
                    Binding = SettingBinding<string?>.ForItem<Trigger>(
                        (_, t) => t.Actions.SpawnTarget ?? NoRoute,
                        (_, t, v) => t.Actions.SpawnTarget = v is null || v == NoRoute ? null : v.Trim(),
                        NewTrigger),
                },
                new HighlightField("fg", "highlight fg", "the colour the matched text is drawn in")
                {
                    Group = DeliveryGroup,
                    Keywords = "colour color foreground",
                    Binding = SettingBinding<TerminalColor?>.ForItem<Trigger>(
                        (_, t) => t.Actions.HighlightForeground, (_, t, v) => t.Actions.HighlightForeground = v, NewTrigger,
                        HighlightColours.Format),
                },
                new HighlightField("bg", "highlight bg", "the colour behind the matched text")
                {
                    Group = DeliveryGroup,
                    Keywords = "colour color background",
                    Binding = SettingBinding<TerminalColor?>.ForItem<Trigger>(
                        (_, t) => t.Actions.HighlightBackground, (_, t, v) => t.Actions.HighlightBackground = v, NewTrigger,
                        HighlightColours.Format),
                },
                new FlagsField("attributes", "attributes", AttributeNames, "added to the matched text")
                {
                    Group = DeliveryGroup,
                    Keywords = "bold underline italic",
                    Binding = SettingBinding<string>.ForItem<Trigger>(
                        (_, t) => FlagWords.Format(t.Actions.AddAttributes),
                        (_, t, v) => t.Actions.AddAttributes = FlagWords.Parse<TextAttributes>(v),
                        NewTrigger),
                },
                ItemFlag<Trigger>("gag", "gag line", DeliveryGroup, t => t.Actions.Gag, (t, v) => t.Actions.Gag = v,
                    "keep the line out of its usual window", "hide suppress", () => NewTrigger()),
                ItemFlag<Trigger>("alert", "alert me", DeliveryGroup, t => t.Actions.Alert, (t, v) => t.Actions.Alert = v,
                    "tell the desktop when this matches while you are in another window", "notify notification bell flash",
                    () => NewTrigger()),

                ItemText<Trigger>("rewrite", "rewrite", TextRule.Template, ActionsGroup,
                    (_, t) => t.Actions.Rewrite, (_, t, v) => t.Actions.Rewrite = v,
                    "replace the line with this; $1..$9 insert captures", NewTrigger, "substitute replace"),
                ItemText<Trigger>("respond", "respond", TextRule.Template, ActionsGroup,
                    (_, t) => t.Actions.SendResponse, (_, t, v) => t.Actions.SendResponse = v,
                    "send this to the game; $1..$9 insert captures", NewTrigger, "send command reply"),
                new TextField("script", "script", Help: "a Lua function to call")
                {
                    Group = ActionsGroup,
                    Rule = TextRule.Template,
                    Keywords = "lua callback function",
                    Suggest = (t, _) => Callbacks(t.Config, ((Trigger)t.Item!).Actions.ScriptCallback),
                    Binding = SettingBinding<string?>.ForItem<Trigger>(
                        (_, t) => t.Actions.ScriptCallback, (_, t, v) => t.Actions.ScriptCallback = v, NewTrigger),
                },

                ItemFlag<Trigger>("stop", "stop processing", BehaviourGroup, t => t.StopProcessing, (t, v) => t.StopProcessing = v,
                    "no later rule sees a line this one matched", "halt last", () => NewTrigger()),
                ItemFlag<Trigger>("case", "case sensitive", BehaviourGroup, t => t.CaseSensitive, (t, v) => t.CaseSensitive = v,
                    "match capitals exactly", "uppercase", () => NewTrigger()),
            ],
        },
    };

    /// <summary>F3: the aliases of every set.</summary>
    public static SettingsSectionContribution Aliases { get; } = new()
    {
        Id = "aliases",
        Title = "Aliases",
        Key = "F3",
        Order = 30,
        Scope = SettingScope.Item,
        Collection = new SettingsCollection<Alias>
        {
            Noun = "alias",
            Plural = "aliases",
            Items = s => s.Aliases,
            SetItems = (s, v) => s.Aliases = v,
            New = (_, _) => NewAlias(),
            Copy = a => a.Clone(),
            Name = a => a.Name,
            Rename = (a, v) => a.Name = v,
            Title = a => a.Pattern,
            Detail = a => $"{a.Pattern} → {a.Substitution.Replace('\n', ';')}",
            Fields =
            [
                ItemFlag<Alias>("enabled", "enabled", RuleGroup, a => a.Enabled, (a, v) => a.Enabled = v,
                    "whether the alias runs", "on off disable", NewAlias),
                ItemText<Alias>("name", "name", TextRule.Name, RuleGroup, (_, a) => a.Name, (_, a, v) => a.Name = v ?? a.Name,
                    "what the alias is called in the list", NewAlias),
                ItemText<Alias>("pattern", "match pattern", TextRule.RequiredPattern, RuleGroup,
                    (_, a) => a.Pattern, (_, a, v) => a.Pattern = v ?? a.Pattern,
                    "a regular expression matched against what you type", NewAlias, "regex command"),
                ItemText<Alias>("expansion", "expansion", TextRule.Lines, RuleGroup,
                    (_, a) => a.Substitution, (_, a, v) => a.Substitution = v ?? a.Substitution,
                    "what is sent instead, one command per line; $1..$9 insert captures", NewAlias, "substitution send"),
                OwnerField<Alias>(s => s.Aliases, NewAlias, () => Aliases!),
                ItemFlag<Alias>("case", "case sensitive", BehaviourGroup, a => a.CaseSensitive, (a, v) => a.CaseSensitive = v,
                    "match capitals exactly", "uppercase", NewAlias),
            ],
        },
    };

    /// <summary>F6: the timers of every set.</summary>
    public static SettingsSectionContribution Timers { get; } = new()
    {
        Id = "timers",
        Title = "Timers",
        Key = "F6",
        Order = 50,
        Scope = SettingScope.Item,
        Collection = new SettingsCollection<TimerDefinition>
        {
            Noun = "timer",
            Plural = "timers",
            CanDuplicate = false,
            Items = s => s.Timers,
            SetItems = (s, v) => s.Timers = v,
            New = (_, _) => NewTimer(),
            Copy = t => new TimerDefinition
            {
                Name = t.Name, IntervalSeconds = t.IntervalSeconds, Command = t.Command, OneShot = t.OneShot,
                Enabled = t.Enabled, ScriptCallback = t.ScriptCallback,
            },
            Name = t => t.Name,
            Rename = (t, v) => t.Name = v,
            Title = t => t.Command,
            Detail = t => $"every {t.IntervalSeconds.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)} s: {t.Command}",
            Fields =
            [
                ItemFlag<TimerDefinition>("enabled", "enabled", RuleGroup, t => t.Enabled, (t, v) => t.Enabled = v,
                    "whether the timer fires", "on off disable", NewTimer),
                ItemText<TimerDefinition>("name", "name", TextRule.Name, RuleGroup, (_, t) => t.Name, (_, t, v) => t.Name = v ?? t.Name,
                    "what the timer is called in the list", NewTimer),
                new DecimalField("interval", "interval", MinIntervalSeconds, MaxIntervalSeconds, "seconds between firings")
                {
                    Group = RuleGroup,
                    Keywords = "period seconds every repeat",
                    Binding = SettingBinding<double>.ForItem<TimerDefinition>(
                        (_, t) => t.IntervalSeconds, (_, t, v) => t.IntervalSeconds = v, NewTimer),
                },
                ItemText<TimerDefinition>("command", "command", TextRule.Required, RuleGroup,
                    (_, t) => t.Command, (_, t, v) => t.Command = v ?? t.Command, "what is sent each time", NewTimer, "send"),
                OwnerField<TimerDefinition>(s => s.Timers, NewTimer, () => Timers!),
                ItemFlag<TimerDefinition>("one-shot", "one-shot", BehaviourGroup, t => t.OneShot, (t, v) => t.OneShot = v,
                    "fire once and stop", "once single", NewTimer),
            ],
        },
    };

    /// <summary>F4: the key bindings of every set.</summary>
    public static SettingsSectionContribution Keypad { get; } = new()
    {
        Id = "keypad",
        Title = "Keypad & hotkeys",
        Key = "F4",
        Order = 40,
        Scope = SettingScope.Item,
        Collection = new SettingsCollection<Macro>
        {
            Noun = "binding",
            Plural = "bindings",
            CanDuplicate = false,
            Items = s => s.Macros,
            SetItems = (s, v) => s.Macros = v,
            New = (context, all) => context.Bindable.FirstOrDefault(k => !all.Any(m => string.Equals(m.Key, k, StringComparison.OrdinalIgnoreCase)))
                is { } key
                ? new Macro { Name = NewBindingName, Key = key, Command = NewBindingCommand }
                : null,
            Copy = m => new Macro { Name = m.Name, Key = m.Key, Enabled = m.Enabled, Command = m.Command, ScriptCallback = m.ScriptCallback },
            Name = m => m.Name,
            Rename = (m, v) => m.Name = v,
            Title = m => m.Key,
            Detail = m => $"{m.Key}: {m.Command}",
            Fields =
            [
                ItemFlag<Macro>("enabled", "enabled", RuleGroup, m => m.Enabled, (m, v) => m.Enabled = v,
                    "whether the binding answers its key", "on off disable", NewMacro),
                ItemText<Macro>("name", "name", TextRule.Name, RuleGroup, (_, m) => m.Name, (_, m, v) => m.Name = v ?? m.Name,
                    "what the binding is called in the list", NewMacro),
                ItemText<Macro>("command", "command", TextRule.Required, RuleGroup,
                    (_, m) => m.Command, (_, m, v) => m.Command = v ?? m.Command, "what the key sends", NewMacro, "send"),
                new KeyField("key", "key", "the chord that runs it; press it to set it")
                {
                    Group = RuleGroup,
                    Keywords = "hotkey chord shortcut macro keypad",
                    Binding = SettingBinding<string>.ForItem<Macro>((_, m) => m.Key, (_, m, v) => m.Key = v, NewMacro),
                },
                OwnerField<Macro>(s => s.Macros, NewMacro, () => Keypad!),
            ],
        },
    };

    /// <summary>
    /// Changes how a rule's pattern is read. Moving <em>to</em> a regex rewrites the pattern as the regex it already
    /// meant, so the rule goes on matching exactly what it matched; moving away leaves the text as typed.
    /// </summary>
    public static void SetMatchType(Trigger trigger, TriggerMatchType type)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (type == TriggerMatchType.Regex && trigger.MatchType != TriggerMatchType.Regex)
        {
            trigger.Pattern = TriggerPatterns.ToRegex(trigger.MatchType, trigger.Pattern);
        }

        trigger.MatchType = type;
    }

    /// <summary>
    /// The routes offered for <paramref name="trigger"/>: <c>(none)</c>, <c>main</c>, every window the host knows, and
    /// the rule's own — suggestions, not the permitted set: a new name makes a new capture window.
    /// </summary>
    public static IReadOnlyList<string> Routes(Trigger trigger, IReadOnlyList<string>? windows)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var routes = new List<string> { NoRoute, TriggerActions.MainWindow };
        foreach (var target in (windows ?? []).Append(trigger.Actions.SpawnTarget ?? NoRoute))
        {
            if (!string.IsNullOrEmpty(target) && !routes.Contains(target, StringComparer.Ordinal))
            {
                routes.Add(target);
            }
        }

        return routes;
    }

    /// <summary>Every script callback the configuration names (and <paramref name="own"/>), as suggestions.</summary>
    public static IReadOnlyList<string> Callbacks(AppConfiguration config, string? own)
    {
        ArgumentNullException.ThrowIfNull(config);
        var named = new List<string>();
        var all = config.TriggerSets.SelectMany(set => set.Triggers.Select(t => t.Actions.ScriptCallback)
                .Concat(set.Aliases.Select(a => a.ScriptCallback))
                .Concat(set.Timers.Select(t => t.ScriptCallback))
                .Concat(set.Macros.Select(m => m.ScriptCallback)))
            .Append(own);
        foreach (var callback in all)
        {
            if (!string.IsNullOrWhiteSpace(callback) && !named.Contains(callback, StringComparer.Ordinal))
            {
                named.Add(callback);
            }
        }

        return named;
    }

    private static Trigger NewTrigger() => new() { Name = NewTriggerName, Pattern = NewTriggerPattern };

    private static Alias NewAlias() =>
        new() { Name = NewAliasName, Pattern = NewAliasPattern, Substitution = NewAliasSubstitution };

    private static TimerDefinition NewTimer() => new()
    {
        Name = NewTimerName, IntervalSeconds = NewTimerIntervalSeconds, Command = NewTimerCommand, Enabled = false,
    };

    private static Macro NewMacro() => new() { Name = NewBindingName, Key = "F12", Command = NewBindingCommand };

    private static BoolField ItemFlag<TItem>(
        string id,
        string label,
        string group,
        Func<TItem, bool> get,
        Action<TItem, bool> set,
        string? help,
        string? keywords,
        Func<TItem> fresh)
        where TItem : class =>
        new(id, label, help)
        {
            Group = group,
            Keywords = keywords,
            Binding = SettingBinding<bool>.ForItem<TItem>((_, i) => get(i), (_, i, v) => set(i, v), fresh, v => v ? "on" : "off"),
        };

    private static TextField ItemText<TItem>(
        string id,
        string label,
        TextRule rule,
        string group,
        Func<AppConfiguration, TItem, string?> get,
        Action<AppConfiguration, TItem, string?> set,
        string? help,
        Func<TItem> fresh,
        string? keywords = null)
        where TItem : class =>
        new(id, label, Help: help)
        {
            Group = group,
            Rule = rule,
            Keywords = keywords,
            Binding = SettingBinding<string?>.ForItem(get, set, fresh),
        };

    private static ChoiceField OwnerField<TItem>(
        Func<TriggerSet, List<TItem>> items, Func<TItem> fresh, Func<SettingsSectionContribution> section)
        where TItem : class =>
        new("set", "set", [], "the trigger set it lives in")
        {
            Group = RuleGroup,
            MovesItem = true,
            Keywords = "trigger set group",
            ChoicesFrom = c => c.TriggerSets.Select(s => s.Name).ToArray(),
            Binding = SettingBinding<string>.ForItem<TItem>(
                (c, i) => c.TriggerSets.FirstOrDefault(s => items(s).Contains(i))?.Name ?? string.Empty,
                (c, i, v) => section().Collection!.MoveTo(c, i, v),
                fresh),
        };
}
