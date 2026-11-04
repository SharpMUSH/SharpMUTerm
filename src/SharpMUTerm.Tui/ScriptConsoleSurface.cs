using SharpMUTerm.Client.Modals;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// The script console in cells: the character's console lines bottom-anchored on the left, its <c>store.*</c> keys on
/// the right, and a one-line Lua prompt. The state is the reader's (what is typed, where in the history, which key is
/// picked, which side ⇥ is on); what it shows is the <see cref="ScriptConsoleModel"/> the window's native console draws.
/// Hosted in a <see cref="ListOverlay"/>, which is sized once, so <see cref="Render(ScriptConsoleModel, string, bool, int, int, int)"/>
/// always draws exactly the rows it is given.
/// </summary>
internal sealed class ScriptConsoleSurface
{
    /// <summary>The width the console asks for; the overlay narrows it on a small terminal.</summary>
    internal const int PreferredWidth = 104;

    /// <summary>Below this width the store is shown in place of the lines (⇥), not beside them.</summary>
    internal const int TwoColumns = 70;

    private int _history = -1;

    public ScriptConsoleSurface(ScriptConsoleModel model) => Model = model;

    public ScriptConsoleModel Model { get; set; }

    /// <summary>What is typed at the prompt.</summary>
    public string Input { get; private set; } = string.Empty;

    /// <summary>Whether ⇥ has put the keys on the store list.</summary>
    public bool OnStore { get; private set; }

    /// <summary>The store key picked.</summary>
    public int StoreSelected { get; private set; }

    /// <summary>What a key asks the client to do.</summary>
    internal enum Ask
    {
        None,
        Run,
        Delete,
        Reload,
    }

    /// <summary>
    /// A key. Returns what the client is to do — run <see cref="Input"/> (then call <see cref="Ran"/>), delete the
    /// picked key, reload — or <see cref="Ask.None"/> when the key only moved or typed.
    /// </summary>
    public Ask Key(ConsoleKeyInfo key)
    {
        var ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        if (ctrl && key.Key == ConsoleKey.L)
        {
            return Ask.Reload;
        }

        if (key.Key == ConsoleKey.Tab)
        {
            OnStore = !OnStore && Model.Store.Count > 0;
            return Ask.None;
        }

        if (OnStore)
        {
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    StoreSelected = Math.Max(0, StoreSelected - 1);
                    return Ask.None;
                case ConsoleKey.DownArrow:
                    StoreSelected = Math.Min(Math.Max(0, Model.Store.Count - 1), StoreSelected + 1);
                    return Ask.None;
                case ConsoleKey.Delete or ConsoleKey.Backspace:
                    return StoreSelected < Model.Store.Count ? Ask.Delete : Ask.None;
            }

            return Ask.None;
        }

        var history = Model.History;
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                return Model.CanRun && Input.Trim().Length > 0 ? Ask.Run : Ask.None;
            case ConsoleKey.Backspace:
                if (Input.Length > 0)
                {
                    Input = Input[..^1];
                }

                return Ask.None;
            case ConsoleKey.UpArrow when history.Count > 0:
                _history = _history < 0 ? history.Count - 1 : Math.Max(0, _history - 1);
                Input = history[_history];
                return Ask.None;
            case ConsoleKey.DownArrow when _history >= 0:
                _history++;
                Input = _history < history.Count ? history[_history] : string.Empty;
                if (_history >= history.Count)
                {
                    _history = -1;
                }

                return Ask.None;
        }

        if (key.KeyChar >= ' ' && !char.IsControl(key.KeyChar)
            && (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0
            && Input.Length < ScriptConsoleModel.MaxInput)
        {
            Input += key.KeyChar;
        }

        return Ask.None;
    }

    /// <summary>The line ran: the prompt empties and the history walk starts again from the newest.</summary>
    public void Ran()
    {
        Input = string.Empty;
        _history = -1;
    }

    /// <summary>The picked key, for <see cref="Ask.Delete"/>.</summary>
    public string? PickedKey => StoreSelected < Model.Store.Count ? Model.Store[StoreSelected].Key : null;

    /// <summary>Keeps the pick inside a store that shrank.</summary>
    public void Clamp()
    {
        StoreSelected = Math.Clamp(StoreSelected, 0, Math.Max(0, Model.Store.Count - 1));
        if (Model.Store.Count == 0)
        {
            OnStore = false;
        }
    }

    /// <summary>What the console draws now.</summary>
    public List<string> Render(int width, int rows) => Render(Model, Input, OnStore, StoreSelected, width, rows);

    /// <summary>The console for <paramref name="model"/>, with <paramref name="input"/> at the prompt, in exactly <paramref name="rows"/> rows.</summary>
    internal static List<string> Render(ScriptConsoleModel model, string input, bool onStore, int storeSelected, int width, int rows)
    {
        width = Math.Max(30, width);
        rows = Math.Max(8, rows);
        var lines = new List<string>
        {
            SpreadLR(
                $"[{ScreenPalette.Accent}]SCRIPT CONSOLE[/]  [{(model.CanRun ? ScreenPalette.Value : ScreenPalette.Caution)}]{Escape(model.Summary)}[/]",
                model.Folder is { } folder ? $"[{ScreenPalette.Label}]{Escape(Fit(folder, Math.Max(10, width / 3)))}[/]" : string.Empty,
                width),
            string.Empty,
        };

        var body = rows - 5;
        var two = width >= TwoColumns;
        var storeWidth = two ? Math.Clamp(width / 3, 26, 40) : width;
        var outWidth = two ? width - storeWidth - 3 : width;

        var output = OutputRows(model, outWidth, body);
        var store = StoreRows(model, onStore, storeSelected, storeWidth, body);
        for (var i = 0; i < body; i++)
        {
            if (two)
            {
                var left = output[i];
                var pad = Math.Max(0, outWidth - VisibleLength(left));
                lines.Add(left + new string(' ', pad) + $" [{ScreenPalette.Rule}]│[/] " + store[i]);
            }
            else
            {
                lines.Add(onStore ? store[i] : output[i]);
            }
        }

        lines.Add(string.Empty);
        lines.Add(model.CanRun
            ? $"[bold {ScreenPalette.Accent}]lua ›[/] [{ScreenPalette.Value}]{Escape(Tail(input, width - 8))}{(onStore ? string.Empty : "▏")}[/]"
            : $"[{ScreenPalette.Label}]lua › no prompt here — {Escape(model.Unavailable ?? string.Empty)}[/]");
        lines.Add($"[{ScreenPalette.Label}]{Escape(Fit(ScriptConsoleModel.Keys, width))}[/]");
        return lines;
    }

    private static List<string> OutputRows(ScriptConsoleModel model, int width, int rows)
    {
        var wrapped = new List<string>();
        foreach (var line in model.Lines)
        {
            var mark = ScriptConsoleModel.Mark(line.Kind);
            var colour = line.Kind switch
            {
                ScriptConsoleKind.Error => ScreenPalette.Warn,
                ScriptConsoleKind.Input => ScreenPalette.Accent,
                ScriptConsoleKind.Result => ScreenPalette.Value,
                ScriptConsoleKind.Note => ScreenPalette.Label,
                _ => ScreenPalette.Muted,
            };
            var first = true;
            foreach (var part in HelpSurface.Wrap(line.Text, Math.Max(4, width - 2)))
            {
                wrapped.Add($"[{colour}]{(first ? mark : " ")} {Escape(part)}[/]");
                first = false;
            }
        }

        if (wrapped.Count == 0)
        {
            wrapped.Add($"[{ScreenPalette.Label}]  {Escape(model.CanRun ? ScriptConsoleModel.Empty : "nothing to show")}[/]");
        }

        // Bottom-anchored: the newest line sits just above the prompt.
        var shown = wrapped.Skip(Math.Max(0, wrapped.Count - rows)).ToList();
        while (shown.Count < rows)
        {
            shown.Insert(0, string.Empty);
        }

        return shown;
    }

    private static List<string> StoreRows(ScriptConsoleModel model, bool onStore, int selected, int width, int rows)
    {
        var list = new List<string>
        {
            $"[{(onStore ? ScreenPalette.Accent : ScreenPalette.Muted)}]STORE[/] [{ScreenPalette.Label}]{model.StoreCount} key{(model.StoreCount == 1 ? string.Empty : "s")} · read only[/]",
        };

        if (model.Store.Count == 0)
        {
            list.Add($"[{ScreenPalette.Label}]  empty — store.set(k, v) keeps one[/]");
        }

        var room = rows - 1 - (model.StoreCount > model.Store.Count ? 1 : 0);
        var first = Math.Clamp(selected - (room / 2), 0, Math.Max(0, model.Store.Count - room));
        var keyWidth = Math.Clamp(model.Store.Count == 0 ? 8 : model.Store.Max(e => e.Key.Length), 4, Math.Max(4, width / 2 - 2));
        for (var i = first; i < model.Store.Count && list.Count < rows; i++)
        {
            var entry = model.Store[i];
            var picked = onStore && i == selected;
            var mark = picked ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            var value = Fit(entry.Value, Math.Max(1, width - keyWidth - 3));
            list.Add($"{mark} [{(picked ? "bold " : string.Empty)}{ScreenPalette.Value}]{Escape(Fit(entry.Key, keyWidth).PadRight(keyWidth))}[/] [{ScreenPalette.Muted}]{Escape(value)}[/]");
        }

        if (model.StoreCount > model.Store.Count && list.Count < rows)
        {
            list.Add($"[{ScreenPalette.Label}]  +{model.StoreCount - model.Store.Count} more[/]");
        }

        while (list.Count < rows)
        {
            list.Add(string.Empty);
        }

        return list;
    }

    private static string Fit(string text, int width) =>
        width <= 0 ? string.Empty : text.Length <= width ? text : width == 1 ? "…" : text[..(width - 1)] + "…";

    private static string Tail(string text, int width) =>
        width <= 1 || text.Length <= width ? text : "…" + text[^(width - 1)..];
}
