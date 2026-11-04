using System.Globalization;
using SharpMUTerm.Core.Import;

namespace SharpMUTerm.Client.Modals;

/// <summary>One line of the import review: what it is, what it is called, and what it says about it.</summary>
/// <param name="Kind">What it is: <c>world</c>, <c>set</c>, or for a left-out entry the kind of rule.</param>
/// <param name="Name">Its name.</param>
/// <param name="Detail">What happens to it, or why it was left out.</param>
public sealed record ImportReviewRow(string Kind, string Name, string Detail);

/// <summary>
/// The import review: what an import from another client would add and what it leaves out, with reasons — the consent
/// step, like the package review (<see cref="Packages.PackageReviewModel"/>). Drawn by the cell surface
/// (<c>ImportReview.Render</c>) and by the window's native modal; nothing is written until ⏎ (or Import), which ends in
/// the client's <c>AcceptImport</c>.
/// <para>
/// It leads with <see cref="ActsHeading"/> — the rules that can send commands as the player — because that is what an
/// import can do that a player should know before saying yes. Passwords the file held are named and are <b>not</b>
/// stored unless <see cref="StorePasswords"/> is switched on here.
/// </para>
/// </summary>
public sealed record ImportReviewModel(
    string Format,
    string Source,
    IReadOnlyList<ImportReviewRow> Adds,
    IReadOnlyList<string> ActsAsYou,
    IReadOnlyList<ImportReviewRow> LeftOut,
    int LeftOutCount,
    int Passwords,
    bool StorePasswords)
{
    /// <summary>How many left-out entries the review lists by name; the rest are counted.</summary>
    public const int ListedLeftOut = 8;

    /// <summary>The heading over the rules that can send commands.</summary>
    public const string ActsHeading = "WHAT CAN ACT AS YOU";

    /// <summary>The heading over what will be added.</summary>
    public const string AddsHeading = "WHAT IT ADDS";

    /// <summary>The heading over what was left out.</summary>
    public const string LeftOutHeading = "LEFT OUT";

    /// <summary>The promise the review makes.</summary>
    public const string Consent = "Nothing is written until you press Enter, and nothing you already have is replaced.";

    /// <summary>The review's title.</summary>
    public string Title => $"Import from {Format}";

    /// <summary>The line under the title.</summary>
    public string Intro => $"Read from {Source}. {Consent}";

    /// <summary>The keys the cell review answers.</summary>
    public string Keys => Passwords > 0 ? "⏎ import · p passwords · Esc cancel" : "⏎ import · Esc cancel";

    /// <summary>What the review says about the passwords the file held, or null when it held none.</summary>
    public string? PasswordLine => Passwords == 0
        ? null
        : StorePasswords
            ? $"{Count(Passwords, "password")} will be stored where every password is kept (the system keychain, else secrets.json)."
            : $"The file holds {Count(Passwords, "password")}, which will not be stored. Store {(Passwords == 1 ? "it" : "them")}?";

    /// <summary>What the review says when there is nothing to add.</summary>
    public bool Empty => Adds.Count == 0;

    /// <summary>The left-out line for the entries past <see cref="ListedLeftOut"/>, or null.</summary>
    public string? MoreLeftOut => LeftOutCount > LeftOut.Count
        ? $"and {(LeftOutCount - LeftOut.Count).ToString(CultureInfo.InvariantCulture)} more"
        : null;

    /// <summary>The review of <paramref name="plan"/>.</summary>
    public static ImportReviewModel From(ImportPlan plan, bool storePasswords = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var result = plan.Result;
        var adds = new List<ImportReviewRow>();
        var listedOn = new List<string>();
        foreach (var world in plan.Worlds)
        {
            var at = $"{world.Host}:{world.Port.ToString(CultureInfo.InvariantCulture)}";
            string detail;
            if (world.Existing)
            {
                detail = world.NewCharacters.Count == 0
                    ? $"already configured ({at}); nothing to add"
                    : $"already configured ({at}); adds {string.Join(", ", world.NewCharacters)}";
            }
            else
            {
                detail = world.NewCharacters.Count == 0
                    ? $"new world, {at}, no character"
                    : $"new world, {at}, {(world.NewCharacters.Count == 1 ? "character" : "characters")} {string.Join(", ", world.NewCharacters)}";
            }

            if (world.KeptCharacters.Count > 0)
            {
                detail += $" · keeps your {string.Join(", ", world.KeptCharacters)} as {(world.KeptCharacters.Count == 1 ? "is" : "they are")}";
            }

            adds.Add(new ImportReviewRow("world", world.Name, detail));
            listedOn.AddRange(world.NewCharacters);
        }

        if (plan.SetName is { } setName)
        {
            var rules = string.Join(", ", new[]
            {
                Count(result.Set.Triggers.Count, "trigger"),
                Count(result.Set.Aliases.Count, "alias", "aliases"),
                Count(result.Set.Timers.Count, "timer"),
                Count(result.Set.Macros.Count, "key"),
            }.Where(s => !s.StartsWith("0 ", StringComparison.Ordinal)));
            var where = listedOn.Count == 0
                ? "new set, listed on no character yet (F5 lists it on one)"
                : $"new set, listed on {string.Join(", ", listedOn)}";
            adds.Add(new ImportReviewRow("set", setName, $"{rules} · {where}"));
        }

        var acts = new List<string>();
        var sendingTriggers = result.Set.Triggers.Count(t => !string.IsNullOrEmpty(t.Actions.SendResponse));
        if (sendingTriggers > 0)
        {
            acts.Add($"{Count(sendingTriggers, "trigger")} {(sendingTriggers == 1 ? "sends" : "send")} a command when a line matches");
        }

        if (result.Set.Aliases.Count > 0)
        {
            acts.Add($"{Count(result.Set.Aliases.Count, "alias", "aliases")} {(result.Set.Aliases.Count == 1 ? "turns" : "turn")} what you type into other commands");
        }

        if (result.Set.Timers.Count > 0)
        {
            acts.Add($"{Count(result.Set.Timers.Count, "timer")} {(result.Set.Timers.Count == 1 ? "sends" : "send")} commands on {(result.Set.Timers.Count == 1 ? "its" : "their")} own while connected");
        }

        if (result.Set.Macros.Count > 0)
        {
            acts.Add($"{Count(result.Set.Macros.Count, "key")} {(result.Set.Macros.Count == 1 ? "sends" : "send")} a command when pressed");
        }

        var leftOut = result.Skipped
            .Take(ListedLeftOut)
            .Select(s => new ImportReviewRow(s.Kind, s.Name, s.Reason))
            .ToArray();
        return new ImportReviewModel(
            ImportFormats.Name(result.Format),
            result.Source,
            adds,
            acts,
            leftOut,
            result.SkippedCount,
            plan.PasswordCount,
            storePasswords && plan.PasswordCount > 0);
    }

    /// <summary>A count and its noun, singular or plural.</summary>
    public static string Count(int count, string noun, string? plural = null) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? noun : plural ?? noun + "s")}";

    public bool Equals(ImportReviewModel? other) =>
        other is not null
        && Format == other.Format
        && Source == other.Source
        && ModelLists.Same(Adds, other.Adds)
        && ModelLists.Same(ActsAsYou, other.ActsAsYou)
        && ModelLists.Same(LeftOut, other.LeftOut)
        && LeftOutCount == other.LeftOutCount
        && Passwords == other.Passwords
        && StorePasswords == other.StorePasswords;

    public override int GetHashCode() =>
        HashCode.Combine(Format, Source, ModelLists.Hash(Adds), ModelLists.Hash(LeftOut), LeftOutCount, Passwords, StorePasswords);
}

/// <summary>
/// One opening of the import review in a host's own controls. <see cref="Accept"/> closes it and writes the import
/// through the client's own method; <see cref="TogglePasswords"/> asks the client to republish with the passwords
/// stored or not; Esc (or a click outside) closes it and writes nothing.
/// </summary>
public sealed class ImportReviewSession : ModalSession<ImportReviewModel>
{
    private readonly Action _accept;
    private readonly Action _passwords;

    public ImportReviewSession(ImportReviewModel model, bool isDark, Action accept, Action passwords, Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(accept);
        ArgumentNullException.ThrowIfNull(passwords);
        _accept = accept;
        _passwords = passwords;
    }

    public override string Surface => "import";

    /// <summary>The host's: import it.</summary>
    public void Accept() => CloseThen(_accept);

    /// <summary>The host's: store the file's passwords, or not. The client republishes the model.</summary>
    public void TogglePasswords()
    {
        if (!IsEnded)
        {
            _passwords();
        }
    }
}
