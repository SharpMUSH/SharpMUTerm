using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Layouts;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// The surface catalog's own rules (<see cref="SurfaceCatalog"/>): each surface declared once, for every host, with a
/// Client model, and drawn somewhere a reader can reach. The window suite's <c>SurfaceParityTests</c> and the
/// terminal suite's <c>SurfaceCellTests</c> hold the hosts to what is declared here.
/// </summary>
public class SurfaceCatalogTests
{
    [Test]
    public async Task EverySurfaceIsDeclaredOnceUnderAStableId()
    {
        var ids = SurfaceCatalog.All.Select(s => s.Id).ToList();
        await Assert.That(ids.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(ids.Count);
        await Assert.That(ids.All(id => id.Length > 0 && id == id.Trim() && id == id.ToLowerInvariant())).IsTrue();
    }

    [Test]
    public async Task EverySurfaceSaysHowEachHostDrawsIt()
    {
        foreach (var surface in SurfaceCatalog.All)
        {
            await Assert.That(surface.Drawing.Keys.Order(StringComparer.Ordinal))
                .IsEquivalentTo(SurfaceHosts.All.Order(StringComparer.Ordinal)).Because(surface.Id);
        }
    }

    /// <summary>A terminal has nothing that can draw over a cell without taking it, so it draws nothing natively.</summary>
    [Test]
    public async Task TheTerminalDrawsNothingNatively()
    {
        foreach (var surface in SurfaceCatalog.All)
        {
            await Assert.That(surface.On(SurfaceHosts.Terminal).HasFlag(SurfaceDrawing.Native)).IsFalse().Because(surface.Id);
        }
    }

    [Test]
    public async Task ANativeSurfaceNamesItsViewsAndACellSurfaceItsRenderer()
    {
        foreach (var surface in SurfaceCatalog.All)
        {
            await Assert.That(surface.HasNative).IsEqualTo(surface.NativeViews.Count > 0).Because(surface.Id);
            await Assert.That(surface.HasCells).IsEqualTo(surface.CellRenderer is not null).Because(surface.Id);

            // Drawn in cells nowhere: the keyboard must reach the same actions, by a command.
            if (!surface.HasCells)
            {
                await Assert.That(surface.KeyboardPath).IsNotNull().Because(surface.Id);
            }
        }
    }

    /// <summary>The model is shared data, so it lives where both hosts can read it: Client or Core, never a UI type.</summary>
    [Test]
    public async Task EveryModelIsAClientOrCoreType()
    {
        var allowed = new[] { typeof(ContributionRegistry).Assembly, typeof(PanelIds).Assembly };
        foreach (var surface in SurfaceCatalog.All)
        {
            await Assert.That(allowed).Contains(surface.Model.Assembly).Because(surface.Id);
        }
    }

    /// <summary>A panel surface's id is the layout's panel id, so a published panel names its surface.</summary>
    [Test]
    public async Task PanelSurfacesAreKnownPanels()
    {
        var panels = SurfaceCatalog.All.Where(s => s.Id is not (SurfaceCatalog.Header or SurfaceCatalog.Sidebar
            or SurfaceCatalog.Settings or SurfaceCatalog.LayoutsMenu or SurfaceCatalog.Palette
            or SurfaceCatalog.Prefix or SurfaceCatalog.RailStrip or SurfaceCatalog.Notice or SurfaceCatalog.Compose
            or SurfaceCatalog.FloatingPanel or SurfaceCatalog.TabOverflow or SurfaceCatalog.Status or SurfaceCatalog.TabStrip or SurfaceCatalog.ScrollBand)
            && !SurfaceCatalog.Modals.Contains(s.Id));
        foreach (var surface in panels)
        {
            await Assert.That(PanelIds.Known).Contains(surface.Id);
        }
    }

    /// <summary>
    /// A native view may offer a door the cells of the same surface do not; it must then say where the cells offer
    /// it — another surface ending in the same method, or a command (a key, a ⌃P entry).
    /// </summary>
    [Test]
    public async Task ANativeOnlyActionNamesWhereTheCellsOfferIt()
    {
        foreach (var surface in SurfaceCatalog.All)
        {
            foreach (var action in surface.Actions.Where(a => a.CellsVia is not null))
            {
                var via = SurfaceCatalog.Find(action.CellsVia!);
                var ok = via is not null
                    ? via.HasCells && via.Actions.Any(a => a.CellsVia is null && a.Reaches == action.Reaches)
                    : action.CellsVia!.Contains(':', StringComparison.Ordinal);
                await Assert.That(ok).IsTrue().Because($"{surface.Id}: {action.Name} → {action.CellsVia}");
            }
        }
    }

    /// <summary>
    /// An action only one host can do (a panel popped out into a window of its own) is declared for that host, which
    /// draws the surface natively, and names the command that reaches it from the cells there.
    /// </summary>
    [Test]
    public async Task AHostOnlyActionNamesItsHostAndItsCellRoute()
    {
        var hostOnly = SurfaceCatalog.All.SelectMany(s => s.Actions.Where(a => a.OnlyOn is not null).Select(a => (Surface: s, Action: a))).ToList();
        await Assert.That(hostOnly).IsNotEmpty();
        foreach (var (surface, action) in hostOnly)
        {
            await Assert.That(SurfaceHosts.All).Contains(action.OnlyOn!).Because($"{surface.Id}: {action.Name}");
            await Assert.That(surface.On(action.OnlyOn!).HasFlag(SurfaceDrawing.Native)).IsTrue().Because($"{surface.Id}: {action.Name}");
            await Assert.That(action.CellsVia).IsNotNull().Because($"{surface.Id}: {action.Name}");
            await Assert.That(action.CellsVia!).Contains(":").Because("the cell route is a command, a ⌃P entry");
        }
    }

    /// <summary>Every panel the window draws natively from a model can pop out, and only the window offers it.</summary>
    [Test]
    public async Task EveryFloatablePanelDeclaresPopOutForTheWindowOnly()
    {
        foreach (var panel in SharpMUTerm.Client.Panels.PanelFloatCommands.Floatable)
        {
            var surface = SurfaceCatalog.Find(panel)!;
            var popOut = surface.Actions.SingleOrDefault(a => a.Name == SharpMUTerm.Client.Panels.PanelFloatCommands.PopOutPrefix);
            await Assert.That(popOut).IsNotNull().Because(panel);
            await Assert.That(popOut!.OnlyOn).IsEqualTo(SurfaceHosts.Window).Because(panel);
        }

        await Assert.That(SurfaceCatalog.Find(SurfaceCatalog.FloatingPanel)!.On(SurfaceHosts.Terminal)).IsEqualTo(SurfaceDrawing.None);
    }
}
