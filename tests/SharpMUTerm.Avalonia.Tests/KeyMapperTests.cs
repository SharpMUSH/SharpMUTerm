using Avalonia.Input;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>The key spellings are AnsiInputParser's, because every key table in the client was written against them.</summary>
public class KeyMapperTests
{
    [Test]
    public async Task CtrlLetterCarriesItsControlByte()
    {
        var key = KeyMapper.FromKeyDown(Key.A, KeyModifiers.Control);
        await Assert.That(key).IsEqualTo(new ConsoleKeyInfo('\x01', ConsoleKey.A, false, false, true));
    }

    [Test]
    public async Task AltLetterIsTheLowercaseLetterWithAlt()
    {
        var key = KeyMapper.FromKeyDown(Key.J, KeyModifiers.Alt);
        await Assert.That(key).IsEqualTo(new ConsoleKeyInfo('j', ConsoleKey.J, false, true, false));
    }

    [Test]
    public async Task AltShiftLetterIsTheCapital()
    {
        // ProcessEscape reads Shift out of char.IsUpper, so an upper-case letter is what Alt+Shift+G is.
        var key = KeyMapper.FromKeyDown(Key.G, KeyModifiers.Alt | KeyModifiers.Shift);
        await Assert.That(key).IsEqualTo(new ConsoleKeyInfo('G', ConsoleKey.G, true, true, false));
    }

    [Test]
    public async Task AltDigitIsTheNumberedJump()
    {
        var key = KeyMapper.FromKeyDown(Key.D3, KeyModifiers.Alt);
        await Assert.That(key).IsEqualTo(new ConsoleKeyInfo('3', ConsoleKey.D3, false, true, false));
    }

    [Test]
    public async Task ModifiedEnterArrivesAsItself()
    {
        // The chords a terminal collapses onto a bare CR, which a window can tell apart.
        await Assert.That(KeyMapper.FromKeyDown(Key.Enter, KeyModifiers.Control))
            .IsEqualTo(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, true));
        await Assert.That(KeyMapper.FromKeyDown(Key.Enter, KeyModifiers.Alt))
            .IsEqualTo(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, true, false));
    }

    [Test]
    public async Task NavigationKeysCarryTheParsersCharacters()
    {
        await Assert.That(KeyMapper.FromKeyDown(Key.Home, KeyModifiers.None))
            .IsEqualTo(new ConsoleKeyInfo('\x1b', ConsoleKey.Home, false, false, false));
        await Assert.That(KeyMapper.FromKeyDown(Key.Up, KeyModifiers.Alt | KeyModifiers.Shift))
            .IsEqualTo(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, true, true, false));
        await Assert.That(KeyMapper.FromKeyDown(Key.F5, KeyModifiers.None))
            .IsEqualTo(new ConsoleKeyInfo('\0', ConsoleKey.F5, false, false, false));
    }

    [Test]
    public async Task APlainLetterIsLeftForTextInput()
    {
        await Assert.That(KeyMapper.FromKeyDown(Key.A, KeyModifiers.None)).IsNull();
        await Assert.That(KeyMapper.FromKeyDown(Key.A, KeyModifiers.Shift)).IsNull();
    }

    [Test]
    public async Task CtrlAltIsLeftForTextInputBecauseItIsAltGr()
    {
        await Assert.That(KeyMapper.FromKeyDown(Key.Q, KeyModifiers.Control | KeyModifiers.Alt)).IsNull();
    }

    [Test]
    public async Task TextInputTypesWhatTheLayoutProduced()
    {
        var keys = KeyMapper.FromText("Aé1").ToList();
        await Assert.That(keys).IsEquivalentTo(new[]
        {
            new ConsoleKeyInfo('A', ConsoleKey.A, true, false, false),
            new ConsoleKeyInfo('é', ConsoleKey.NoName, false, false, false),
            new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false),
        });
    }

    [Test]
    public async Task TextInputDropsControlCharacters()
    {
        await Assert.That(KeyMapper.FromText("\x01\r").ToList()).IsEmpty();
    }
}
