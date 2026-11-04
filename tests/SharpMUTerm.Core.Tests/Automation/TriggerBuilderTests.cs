using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Tests.Automation;

/// <summary>
/// The trigger builder's Core half: the plain-language match kinds, the pattern proposed from a selection,
/// and the live test F2 runs over held output.
/// </summary>
public class TriggerBuilderTests
{
    [Test]
    public async Task NumbersBecomeWildcards()
    {
        var suggestion = TriggerSuggestion.Suggest("You gain 125 experience points.");

        await Assert.That(suggestion.MatchType).IsEqualTo(TriggerMatchType.Wildcard);
        await Assert.That(suggestion.Pattern).IsEqualTo("You gain * experience points.");
        await Assert.That(suggestion.Regex).IsEqualTo("You gain (.*?) experience points\\.");
    }

    [Test]
    public async Task NumbersWithSeparatorsAreOneWildcard()
    {
        var suggestion = TriggerSuggestion.Suggest("HP: 1,024/2,048 (3.5%)");

        await Assert.That(suggestion.Pattern).IsEqualTo("HP: */* (*%)");
    }

    [Test]
    public async Task QuotedSpeechKeepsItsQuotesAndLosesItsWords()
    {
        var suggestion = TriggerSuggestion.Suggest("Rivane says, \"to the crypt, then!\"");

        await Assert.That(suggestion.Pattern).IsEqualTo("Rivane says, \"*\"");
    }

    [Test]
    public async Task KnownPlayerNamesBecomeWildcardsOnlyAsWholeWords()
    {
        var suggestion = TriggerSuggestion.Suggest(
            "Rivane tells you: meet Rivanes at dawn", new[] { "Rivane" });

        await Assert.That(suggestion.Pattern).IsEqualTo("* tells you: meet Rivanes at dawn");
    }

    [Test]
    public async Task ALineWithNothingVariableIsMatchedAsText()
    {
        var suggestion = TriggerSuggestion.Suggest("  A town guard nods to you.  \nsecond line");

        await Assert.That(suggestion.MatchType).IsEqualTo(TriggerMatchType.Contains);
        await Assert.That(suggestion.Pattern).IsEqualTo("A town guard nods to you.");
        await Assert.That(suggestion.Name).IsEqualTo("A town guard nods to you");
    }

    [Test]
    public async Task ALineThatIsAllVariableIsMatchedAsTextRatherThanAsEverything()
    {
        var suggestion = TriggerSuggestion.Suggest("12345");

        await Assert.That(suggestion.MatchType).IsEqualTo(TriggerMatchType.Contains);
        await Assert.That(suggestion.Pattern).IsEqualTo("12345");
    }

    [Test]
    public async Task ALiteralAsteriskIsEscapedSoItStillMatchesItself()
    {
        var suggestion = TriggerSuggestion.Suggest("*** You have 3 new mail ***");
        var trigger = new Trigger { Pattern = suggestion.Pattern, MatchType = suggestion.MatchType };

        await Assert.That(suggestion.Pattern).IsEqualTo("\\*\\*\\* You have * new mail \\*\\*\\*");
        await Assert.That(trigger.Regex.IsMatch("*** You have 12 new mail ***")).IsTrue();
        await Assert.That(trigger.Regex.IsMatch("You have 12 new mail")).IsFalse();
    }

    [Test]
    public async Task AWildcardIsACaptureAndATrailingOneIsGreedy()
    {
        var trigger = new Trigger { Pattern = "* says *", MatchType = TriggerMatchType.Wildcard };

        var match = trigger.Regex.Match("Rivane says hello there");

        await Assert.That(match.Groups[1].Value).IsEqualTo("Rivane");
        await Assert.That(match.Groups[2].Value).IsEqualTo("hello there");
    }

    [Test]
    public async Task ContainsReadsRegexCharactersLiterally()
    {
        var trigger = new Trigger { Pattern = "[public] (", MatchType = TriggerMatchType.Contains };

        await Assert.That(trigger.Regex.IsMatch("x [public] (hi)")).IsTrue();
        await Assert.That(trigger.Regex.IsMatch("p")).IsFalse();
    }

    [Test]
    public async Task StartsWithIsAnchoredAndCaseFollowsTheRule()
    {
        var trigger = new Trigger { Pattern = "You gain", MatchType = TriggerMatchType.StartsWith };

        await Assert.That(trigger.Regex.IsMatch("you gain 5")).IsTrue();
        await Assert.That(trigger.Regex.IsMatch("Then you gain 5")).IsFalse();

        trigger.CaseSensitive = true;
        await Assert.That(trigger.Regex.IsMatch("you gain 5")).IsFalse();
    }

    [Test]
    public async Task ChangingTheMatchTypeRecompiles()
    {
        var trigger = new Trigger { Pattern = "a.c" };
        await Assert.That(trigger.Regex.IsMatch("abc")).IsTrue();

        trigger.MatchType = TriggerMatchType.Contains;

        await Assert.That(trigger.Regex.IsMatch("abc")).IsFalse();
        await Assert.That(trigger.Regex.IsMatch("xa.cx")).IsTrue();
    }

    [Test]
    public async Task OnlyARegexCanFailToParse()
    {
        await Assert.That(TriggerPatterns.Validate(TriggerMatchType.Regex, "(")).IsNotNull();
        await Assert.That(TriggerPatterns.Validate(TriggerMatchType.Contains, "(")).IsNull();
        await Assert.That(TriggerPatterns.Validate(TriggerMatchType.Wildcard, "* *")).IsNotNull();
        await Assert.That(TriggerPatterns.Validate(TriggerMatchType.StartsWith, string.Empty)).IsNotNull();
    }

    [Test]
    public async Task ThePlainWordsRoundTripAndComeBeforeTheRegex()
    {
        foreach (var type in Enum.GetValues<TriggerMatchType>())
        {
            await Assert.That(TriggerPatterns.Parse(TriggerPatterns.Describe(type))).IsEqualTo(type);
        }

        await Assert.That(TriggerPatterns.Offered[^1]).IsEqualTo(TriggerMatchType.Regex);
    }

    [Test]
    public async Task TheMatchTypeRoundTripsThroughTheConfigurationAndDefaultsToRegex()
    {
        var config = new AppConfiguration();
        config.TriggerSets.Add(new TriggerSet
        {
            Name = "Mine",
            Triggers =
            {
                new Trigger { Name = "xp", Pattern = "You gain * xp", MatchType = TriggerMatchType.Wildcard },
                new Trigger { Name = "old", Pattern = "^old$" },
            },
        });

        var loaded = ConfigurationStore.Deserialize(ConfigurationStore.Serialize(config));
        var legacy = ConfigurationStore.Deserialize("""
            { "version": 5, "triggerSets": [ { "name": "S", "triggers": [ { "name": "t", "pattern": "^a" } ] } ] }
            """);

        await Assert.That(loaded.TriggerSets[0].Triggers[0].MatchType).IsEqualTo(TriggerMatchType.Wildcard);
        await Assert.That(loaded.TriggerSets[0].Triggers[1].MatchType).IsEqualTo(TriggerMatchType.Regex);
        await Assert.That(legacy.TriggerSets[0].Triggers[0].MatchType).IsEqualTo(TriggerMatchType.Regex);
        await Assert.That(new Trigger { Pattern = "x", MatchType = TriggerMatchType.StartsWith }.Clone().MatchType)
            .IsEqualTo(TriggerMatchType.StartsWith);
    }

    [Test]
    public async Task ThePreviewCountsEveryMatchAndKeepsTheNewestFew()
    {
        var lines = new[] { "You gain 1 xp", "noise", "You gain 2 xp", "You gain 3 xp", "You gain 4 xp" };

        var result = TriggerPreview.Run(lines, TriggerMatchType.Wildcard, "You gain * xp", caseSensitive: false);

        await Assert.That(result.Problem).IsNull();
        await Assert.That(result.Tested).IsEqualTo(5);
        await Assert.That(result.Matched).IsEqualTo(4);
        await Assert.That(result.Samples.Select(s => s.Text))
            .IsEquivalentTo(new[] { "You gain 4 xp", "You gain 3 xp", "You gain 2 xp" });
    }

    [Test]
    public async Task ThePreviewTriesOnlyTheNewestLines()
    {
        var lines = Enumerable.Range(0, TriggerPreview.MaxLines + 500).Select(i => i < 500 ? "old" : "new").ToArray();

        var result = TriggerPreview.Run(lines, TriggerMatchType.Contains, "old", caseSensitive: false);

        await Assert.That(result.Tested).IsEqualTo(TriggerPreview.MaxLines);
        await Assert.That(result.Matched).IsEqualTo(0);
    }

    [Test]
    public async Task AnUnparseablePatternIsAStateNotAThrow()
    {
        var result = TriggerPreview.Run(new[] { "a" }, TriggerMatchType.Regex, "(unclosed", caseSensitive: false);

        await Assert.That(result.Problem).IsNotNull();
        await Assert.That(result.Matched).IsEqualTo(0);
    }
}
