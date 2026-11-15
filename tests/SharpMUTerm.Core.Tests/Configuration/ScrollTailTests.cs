using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Tests.Configuration;

public class ScrollTailTests
{
    [Test]
    public async Task AFreshConfigurationAsksForSixRows()
    {
        await Assert.That(new AppConfiguration().Text.ScrollTail).IsEqualTo("6");
        await Assert.That(ScrollTail.Rows(new AppConfiguration().Text.ScrollTail, 29)).IsEqualTo(6);
    }

    [Test]
    [Arguments("6", 29, 6)]
    [Arguments(" 12 ", 29, 12)]
    [Arguments("50%", 29, 15)]
    [Arguments("25%", 29, 7)]
    [Arguments("25 %", 40, 10)]
    [Arguments("100%", 29, 29)]
    public async Task RowsOrAShareOfWhatIsAvailable(string text, int available, int rows)
    {
        await Assert.That(ScrollTail.Problem(text)).IsNull();
        await Assert.That(ScrollTail.Rows(text, available)).IsEqualTo(rows);
    }

    [Test]
    [Arguments("")]
    [Arguments("0")]
    [Arguments("0%")]
    [Arguments("101")]
    [Arguments("101%")]
    [Arguments("-3")]
    [Arguments("2.5")]
    [Arguments("%")]
    [Arguments("half")]
    [Arguments("١٢")]
    public async Task AnythingElseIsRefusedAndReadAsTheDefault(string text)
    {
        await Assert.That(ScrollTail.Problem(text)).IsNotNull();
        await Assert.That(ScrollTail.Rows(text, 29)).IsEqualTo(6);
    }

    [Test]
    [Arguments(" 25 % ", "25%")]
    [Arguments("007", "7")]
    [Arguments("8", "8")]
    public async Task AValueIsStoredInOneSpelling(string text, string stored)
    {
        await Assert.That(ScrollTail.Normalise(text)).IsEqualTo(stored);
    }
}
