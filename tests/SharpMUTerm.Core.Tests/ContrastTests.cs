using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests;

/// <summary>
/// The legibility floor: <see cref="Contrast.Legible"/> is what stops this client painting a colour
/// nobody can read on the plane it lands on. The reported defect these pin is the freeze bar's
/// <c>#800080</c> on a focused dark pane — 1.27:1, which is very nearly the same colour twice.
/// </summary>
public class ContrastTests
{
    /// <summary>The brightest plane a pane can wear on the default dark theme — §2's worst case.</summary>
    private static readonly Rgb DarkPane = new(0x36, 0x36, 0x3d);

    private static readonly Rgb LightPane = new(0xf2, 0xf2, 0xf5);

    private const double Floor = Contrast.Floor;

    [Test]
    public async Task WhiteOnBlackIsTheDefinitionsOwnMaximum()
    {
        // 21:1 is the ratio the WCAG definition tops out at; getting it exactly is the cheapest
        // evidence that the luminance curve here is the standard one and not an approximation of it.
        var ratio = Contrast.Ratio(new Rgb(0xff, 0xff, 0xff), new Rgb(0, 0, 0));

        await Assert.That(ratio).IsBetween(20.99, 21.01);
    }

    [Test]
    public async Task ARatioIsTheSameWhicheverWayRoundItIsAsked()
    {
        var a = new Rgb(0x12, 0x34, 0x56);
        var b = new Rgb(0xcd, 0xef, 0x01);

        await Assert.That(Contrast.Ratio(a, b)).IsEqualTo(Contrast.Ratio(b, a)).Within(1e-9);
    }

    [Test]
    public async Task TheFreezeBarsOwnColourIsBelowTheFloorAndComesBackAboveIt()
    {
        // The reported defect, as a number: ANSI 5 on the plane the bar is actually drawn on.
        var magenta = AnsiPalette.ToRgb(5);
        await Assert.That(Contrast.Ratio(magenta, DarkPane)).IsLessThan(1.3);

        var lifted = Contrast.Legible(magenta, DarkPane, Floor);

        await Assert.That(Contrast.Ratio(lifted, DarkPane)).IsGreaterThanOrEqualTo(Floor);
    }

    [Test]
    public async Task AColourThatAlreadyClearsTheFloorIsHandedBackUntouched()
    {
        // Not "close enough": byte-identical, so turning the floor on cannot quietly restyle text that
        // was already fine — which is most of what a game sends.
        var gold = new Rgb(0xff, 0xd7, 0x00);

        await Assert.That(Contrast.Legible(gold, DarkPane, Floor)).IsEqualTo(gold);
    }

    [Test]
    public async Task LiftingIsIdempotent()
    {
        var once = Contrast.Legible(new Rgb(0x00, 0x00, 0x80), DarkPane, Floor);
        var twice = Contrast.Legible(once, DarkPane, Floor);

        await Assert.That(twice).IsEqualTo(once);
    }

    [Test]
    public async Task ADarkPlaneLiftsAndALightPlaneDarkens()
    {
        // The direction is the plane's, not the colour's — one function for every theme.
        var navy = new Rgb(0x00, 0x00, 0x80);
        var gold = new Rgb(0xff, 0xd7, 0x00);

        var onDark = Contrast.Legible(navy, DarkPane, Floor);
        var onLight = Contrast.Legible(gold, LightPane, Floor);

        await Assert.That(Contrast.RelativeLuminance(onDark))
            .IsGreaterThan(Contrast.RelativeLuminance(navy));
        await Assert.That(Contrast.RelativeLuminance(onLight))
            .IsLessThan(Contrast.RelativeLuminance(gold));
    }

    [Test]
    public async Task TheLiftStopsAtTheFloorRatherThanRunningToWhite()
    {
        // A floor, not a wash: a colour lifted past what it needed has thrown away the hue it was
        // carrying for no gain. Half a point of slack is the search's own resolution.
        var lifted = Contrast.Legible(new Rgb(0x80, 0x00, 0x80), DarkPane, Floor);
        var ratio = Contrast.Ratio(lifted, DarkPane);

        await Assert.That(ratio).IsGreaterThanOrEqualTo(Floor);
        await Assert.That(ratio).IsLessThan(Floor + 0.5);
    }

    [Test]
    public async Task HueSurvivesTheLiftWhileThereIsHeadroomForIt()
    {
        // Blending toward white keeps the channel *order*, which is what makes a lifted colour still
        // recognisably the colour the server sent. Red stays reddest; blue stays bluest.
        var maroon = new Rgb(0x80, 0x00, 0x00);
        var lifted = Contrast.Legible(maroon, DarkPane, Floor);

        await Assert.That(lifted.R).IsGreaterThan(lifted.G);
        await Assert.That(lifted.R).IsGreaterThan(lifted.B);
    }

    [Test]
    public async Task APureBlueDesaturatesBecauseHueAloneCannotReachTheFloor()
    {
        // #0000ff has a relative luminance of 0.0722 and so tops out at 1.88:1 on a dark pane at full
        // blue. A rule that held hue absolutely would leave it unreadable, which is the defect. It has
        // to bring the other channels up — and this is the test that says so out loud, so nobody
        // "fixes" the desaturation later.
        var blue = new Rgb(0x00, 0x00, 0xff);

        var lifted = Contrast.Legible(blue, DarkPane, Floor);

        await Assert.That(Contrast.Ratio(lifted, DarkPane)).IsGreaterThanOrEqualTo(Floor);
        await Assert.That(lifted.B).IsGreaterThan(lifted.R);
        await Assert.That(lifted.R).IsGreaterThan((byte)0);
    }

    [Test]
    public async Task BlackOnBlackReachesTheFloorToo()
    {
        // The degenerate case: no hue to preserve at all, so the lift is pure greying-up. It has to
        // terminate rather than divide by a zero luminance.
        var lifted = Contrast.Legible(new Rgb(0, 0, 0), new Rgb(0, 0, 0), Floor);

        await Assert.That(Contrast.Ratio(lifted, new Rgb(0, 0, 0))).IsGreaterThanOrEqualTo(Floor);
    }

    [Test]
    public async Task AFloorThatCannotBeReachedGivesTheFurthestColourRatherThanThrowing()
    {
        // 21:1 against anything but pure black is arithmetically impossible. The contract is best
        // effort, because the caller is a render path fed from the telnet read loop and there is
        // nothing useful it could do with a throw.
        var plane = new Rgb(0x40, 0x40, 0x40);

        var lifted = Contrast.Legible(new Rgb(0x20, 0x20, 0x20), plane, 21.0);

        await Assert.That(lifted).IsEqualTo(new Rgb(0xff, 0xff, 0xff));
    }

    [Test]
    public async Task ThePivotIsLuminanceAndNotTheByteValue()
    {
        // #808080 looks like "half way" written in bytes and is *not*: its relative luminance is 0.216,
        // comfortably above the 0.18 mid-scale, so it is a light plane and a foreground on it darkens.
        // Pivoting on the bytes instead would call it dark and lift text *toward* it.
        var lifted = Contrast.Legible(new Rgb(0x7a, 0x7a, 0x7a), new Rgb(0x80, 0x80, 0x80), Floor);

        await Assert.That(Contrast.RelativeLuminance(lifted))
            .IsLessThan(Contrast.RelativeLuminance(new Rgb(0x7a, 0x7a, 0x7a)));
    }
}
