using System.Text;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Text;

/// <summary>The composer's spelling: a Hunspell dictionary, a MUSH post's markup left alone, and words added by hand.</summary>
public class SpellCheckerTests
{
    internal const string Aff = "SET UTF-8\nTRY esianrtolcdugmphbyfvkwz'\n";
    internal const string Dic = "7\nhello\nworld\nthe\ncolour\ndon't\nsay\nme\n";

    internal static SpellChecker Open(string? userWords = null) =>
        SpellChecker.FromStreams(new MemoryStream(Encoding.UTF8.GetBytes(Dic)), new MemoryStream(Encoding.UTF8.GetBytes(Aff)), "en_GB", userWords);

    private static string[] Marked(SpellChecker checker, string line) =>
        checker.Misspellings(line).Select(m => line.Substring(m.Start, m.Length)).ToArray();

    [Test]
    public async Task UnknownWordsAreMarkedWhereTheyAre()
    {
        var checker = Open();
        var line = "hello wrold, the colur";

        await Assert.That(Marked(checker, line)).IsEquivalentTo(new[] { "wrold", "colur" });
        await Assert.That(checker.Misspellings(line)[0]).IsEqualTo(new Misspelling(6, 5));
    }

    [Test]
    public async Task APostsMarkupIsNotProse()
    {
        var checker = Open();

        // %r is a break and the word after it is checked; a function name, a dbref, an acronym, a camel-cased
        // name and a single letter are not words to mark; an inner apostrophe keeps a word whole.
        await Assert.That(Marked(checker, "hello%rworld %rwrold")).IsEquivalentTo(new[] { "wrold" });
        await Assert.That(Marked(checker, "[pemit(me, hello)] #1234 OOC McTavish a don't")).IsEmpty();
    }

    [Test]
    public async Task AnAddedWordIsNeverMarkedAgainAndIsKeptForTheNextLaunch()
    {
        var root = Directory.CreateTempSubdirectory("sharpmuterm-spell-").FullName;
        try
        {
            var words = Path.Combine(root, "dictionaries", "user-words.txt");
            var checker = Open(words);
            await Assert.That(checker.Add("Corvid")).IsTrue();
            await Assert.That(checker.Add("two words")).IsFalse();

            await Assert.That(Marked(checker, "say Corvid")).IsEmpty();
            await Assert.That(Marked(Open(words), "say corvid")).IsEmpty();
            await Assert.That(File.ReadAllText(words)).IsEqualTo("Corvid\n");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task TheWordAtTheCaretIncludesACaretJustPastIt()
    {
        await Assert.That(SpellChecker.WordAt("say wrold now", 5)).IsEqualTo("wrold");
        await Assert.That(SpellChecker.WordAt("say wrold now", 9)).IsEqualTo("wrold");
        await Assert.That(SpellChecker.WordAt("say  now", 4)).IsNull();
    }

    [Test]
    public async Task TheCulturesDictionaryWinsThenItsLanguageThenNone()
    {
        var root = Directory.CreateTempSubdirectory("sharpmuterm-spell-").FullName;
        try
        {
            var empty = Path.Combine(root, "empty");
            var full = Path.Combine(root, "full");
            Directory.CreateDirectory(empty);
            Directory.CreateDirectory(full);
            foreach (var name in new[] { "de_DE", "en_GB", "en_US" })
            {
                File.WriteAllText(Path.Combine(full, name + ".aff"), Aff);
                File.WriteAllText(Path.Combine(full, name + ".dic"), Dic);
            }

            File.WriteAllText(Path.Combine(full, "fr_FR.dic"), Dic); // no .aff beside it, so not a dictionary

            await Assert.That(SpellChecker.Find([empty, full], "en-US", null)!.Name).IsEqualTo("en_US");
            await Assert.That(SpellChecker.Find([full], "en_AU", null)!.Name).IsEqualTo("en_GB");
            await Assert.That(SpellChecker.Find([full], "fr_FR", null)!.Name).IsEqualTo("de_DE");
            await Assert.That(SpellChecker.Find([full], null, null)!.Name).IsEqualTo("de_DE");
            await Assert.That(SpellChecker.Find([empty, Path.Combine(root, "missing")], "en-US", null)).IsNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
