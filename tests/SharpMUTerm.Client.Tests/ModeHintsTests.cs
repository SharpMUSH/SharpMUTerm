using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Contributions;

namespace SharpMUTerm.Client.Tests;

public class ModeHintsTests
{
    [Test]
    public async Task NoModeNamesNoKeys()
    {
        await Assert.That(ModeHints.For(StatusMode.None)).IsEmpty();
        await Assert.That(ModeHints.Candidates(StatusMode.None, HostKeyGlyphs.Terminal)).IsEmpty();
    }

    [Test]
    [Arguments(StatusMode.ScrolledBack)]
    [Arguments(StatusMode.SplitScrollback)]
    [Arguments(StatusMode.Frozen)]
    [Arguments(StatusMode.SearchResult)]
    [Arguments(StatusMode.Composer)]
    public async Task EveryModeNamesAtLeastItsWayOutAndCandidatesShrinkFromTheEnd(StatusMode mode)
    {
        var candidates = ModeHints.Candidates(mode, HostKeyGlyphs.Terminal);
        await Assert.That(candidates.Count).IsEqualTo(ModeHints.For(mode).Count);
        await Assert.That(candidates.Count).IsGreaterThan(0);
        for (var i = 1; i < candidates.Count; i++)
        {
            await Assert.That(candidates[i - 1]).StartsWith(candidates[i]).Because("a narrower row loses the last key, never the first");
            await Assert.That(candidates[i].Length).IsLessThan(candidates[i - 1].Length);
        }
    }

    [Test]
    public async Task TheKeysAreTheOnesTheModesAnswer()
    {
        await Assert.That(ModeHints.Candidates(StatusMode.SplitScrollback, HostKeyGlyphs.Terminal)[0]).IsEqualTo("End returns · ⌥F freezes");
        await Assert.That(ModeHints.Candidates(StatusMode.Frozen, HostKeyGlyphs.Terminal)[0]).IsEqualTo("⌥F thaws");
        await Assert.That(ModeHints.Candidates(StatusMode.SearchResult, HostKeyGlyphs.Terminal)[^1]).IsEqualTo("⌥G next hit");
        await Assert.That(ModeHints.Candidates(StatusMode.Composer, HostKeyGlyphs.Terminal)[0])
            .IsEqualTo("⌃S sends · Esc releases · F1 full screen");
    }

    /// <summary>A key is written in the palette's one vocabulary, so a host's word spelling reads as a glyph.</summary>
    [Test]
    public async Task AHostSpelledKeyIsNormalisedToGlyphs()
    {
        var hint = new ModeHint(KeyHint.ForHost(HostKey.Newline), "breaks the line");
        await Assert.That(hint.Text(new HostKeyGlyphs("Ctrl+⏎"))).IsEqualTo("⌃⏎ breaks the line");
        await Assert.That(hint.Text(HostKeyGlyphs.Terminal)).IsEqualTo("⌥⏎ breaks the line");
    }
}
