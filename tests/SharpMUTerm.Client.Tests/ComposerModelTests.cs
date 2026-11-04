using SharpMUTerm.Client.Composer;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// The composer's model both hosts draw (<see cref="ComposerModel"/>), the marks a dictionary puts on a whole buffer
/// (<see cref="ComposerMarks"/>), and the channel a host's editor talks back through (<see cref="ComposerSession"/>).
/// </summary>
public class ComposerModelTests
{
    private static SpellChecker Dictionary() =>
        SpellChecker.FromStreams(new MemoryStream("4\nhello\nthe\nworld\npose\n"u8.ToArray()), new MemoryStream("SET UTF-8\n"u8.ToArray()), "en_GB");

    private static ComposerModel Post(string body, ComposeEscaping escaping = ComposeEscaping.AsTyped, SpellChecker? spelling = null) =>
        ComposerModel.Create(ComposerPlace.FullScreen, "Ann", body, escaping, null, spelling, canSend: true, armed: true, revision: 1);

    [Test]
    public async Task TheCommandIsComposeMessageBuildOfTheDraft()
    {
        foreach (var escaping in new[] { ComposeEscaping.AsTyped, ComposeEscaping.Literal })
        {
            const string draft = "+bbpost 1=Title\n\nFirst, 50% [done].\n";
            await Assert.That(Post(draft, escaping).Command).IsEqualTo(ComposeMessage.Build(draft, escaping));
        }
    }

    [Test]
    public async Task AMooEditHasNoCommandNoDictionaryAndOnlySaveAndClose()
    {
        var model = ComposerModel.Create(ComposerPlace.FullScreen, "Ann", "return 1;", ComposeEscaping.Literal,
            new ComposerEdit("Test:look", "moo-code"), Dictionary(), canSend: true, armed: true, revision: 1);

        await Assert.That(model.Command).IsNull();
        await Assert.That(model.Dictionary).IsNull();
        await Assert.That(model.Marks).IsEmpty();
        await Assert.That(model.Keys.Select(k => k.Key)).IsEquivalentTo(new[] { "⌃S", "Esc" });
        await Assert.That(model.Keys[0].Label).IsEqualTo("save");
    }

    [Test]
    public async Task LineEndingsAreTheClientsBreaks()
    {
        await Assert.That(Post("a\r\nb").Body).IsEqualTo("a\nb");
    }

    [Test]
    public async Task TwoModelsBuiltFromTheSameDraftAreEqual()
    {
        var spelling = Dictionary();
        var a = Post("hello wrold\nthe pose", spelling: spelling);
        var b = Post("hello wrold\nthe pose", spelling: spelling);

        await Assert.That(a).IsEqualTo(b);
        await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
        await Assert.That(a).IsNotEqualTo(a with { Revision = 2 });
        await Assert.That(a).IsNotEqualTo(Post("hello wrold\nthe pose", ComposeEscaping.Literal, spelling));
    }

    [Test]
    public async Task MarksAreRangesOfTheWholeBufferOnEveryLine()
    {
        var body = "hello wrold\nthe pose zork";
        var marks = ComposerMarks.For(body, Dictionary());

        await Assert.That(marks.Select(m => body.Substring(m.Start, m.Length))).IsEquivalentTo(new[] { "wrold", "zork" });
        await Assert.That(ComposerMarks.For(body, null)).IsEmpty();
    }

    [Test]
    public async Task TheWordAtTheCaretIsTheOneItIsInOrJustAfter()
    {
        const string body = "hello wrold\nthe zork";
        await Assert.That(ComposerMarks.WordAt(body, 8)).IsEqualTo("wrold");
        await Assert.That(ComposerMarks.WordAt(body, 11)).IsEqualTo("wrold").Because("just after the word, at the line's end");
        await Assert.That(ComposerMarks.WordAt(body, body.Length)).IsEqualTo("zork");
        await Assert.That(ComposerMarks.WordAt(body, 0)).IsEqualTo("hello");
    }

    [Test]
    public async Task TheDockedKeyRowSaysWhatTheCellsSay()
    {
        await Assert.That(ComposerModel.KeysFor(ComposerPlace.Docked, false, false, true, armed: false).Select(k => k.Label))
            .IsEquivalentTo(new[] { "click here to write", "full screen" });
        await Assert.That(ComposerModel.KeysFor(ComposerPlace.Docked, false, true, true, armed: true).Select(k => k.Key))
            .IsEquivalentTo(new[] { "⌃S", "⌥L", "⌥W", "F1", "Esc" });
    }

    [Test]
    public async Task ASessionPassesGesturesOnUntilItEnds()
    {
        var sent = new List<string>();
        var edited = new List<string>();
        var session = new ComposerSession(Post(string.Empty), null, new ComposerCallbacks
        {
            Send = sent.Add,
            Edited = edited.Add,
        });

        session.Edited("a\r\nb");
        session.Send("a\nb");
        session.End();
        session.Send("late");

        await Assert.That(edited).IsEquivalentTo(new[] { "a\nb" });
        await Assert.That(sent).IsEquivalentTo(new[] { "a\nb" });
        await Assert.That(session.IsEnded).IsTrue();
    }

    [Test]
    public async Task ASessionRaisesChangedOnlyWhenTheModelDiffers()
    {
        var session = new ComposerSession(Post("x"), null, new ComposerCallbacks());
        var raised = 0;
        session.Changed += _ => raised++;

        session.Publish(Post("x"));
        session.Publish(Post("y"));

        await Assert.That(raised).IsEqualTo(1);
        await Assert.That(session.Model.Body).IsEqualTo("y");
    }

    [Test]
    public async Task ASessionMarksWithTheClientsDictionaryOnlyOnceTheModelSaysOne()
    {
        var spelling = Dictionary();
        var none = new ComposerSession(Post("wrold"), () => spelling, new ComposerCallbacks());
        var some = new ComposerSession(Post("wrold", spelling: spelling), () => spelling, new ComposerCallbacks());

        await Assert.That(none.Marks("wrold")).IsEmpty().Because("the model says no dictionary is open");
        await Assert.That(some.Marks("hello wrold")).IsEquivalentTo(new[] { new SpellRange(6, 5) });
        await Assert.That(some.Suggestions("wrold")).Contains("world");
        await Assert.That(some.CanAddWords).IsTrue();
        await Assert.That(none.CanAddWords).IsFalse();
    }
}
