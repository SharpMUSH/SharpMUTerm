using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Styling;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The keyboard's focus ring, one look on every native surface: the tokens' <see cref="NativeTokens.Focus"/> edge (<see cref="NativeTokens.FocusRingInk"/>), held to
/// <see cref="NativeTokens.FocusRingFloor"/> against every plane a control sits on, drawn
/// <see cref="NativeTokens.FocusRingWidth"/> px wide on the control's own edge, inside it, and rounded as the control is: the adorner layer clips an adorner
/// to what is visible of its control, so a ring outside the edge is never seen (and one inside respects a scroll). It is the
/// control's <see cref="Control.FocusAdorner"/>, so Avalonia shows it only when the keyboard put the focus there (Tab, an
/// arrow), never on a click.
/// </summary>
internal sealed class FocusRing : Border
{
    /// <summary>The resource a scope's ring colour is read from (<see cref="NativeTokens.ApplyFocusRing"/>).</summary>
    public const string BrushKey = "SharpMUTermFocusRingBrush";

    /// <summary>The resource the ring's inner hairline is read from: the scope's card, so the ring reads on an accent fill.</summary>
    public const string InnerKey = "SharpMUTermFocusRingInnerBrush";

    private readonly Border _inner;

    public FocusRing()
    {
        BorderThickness = new Thickness(NativeTokens.FocusRingWidth);
        IsHitTestVisible = false;
        CornerRadius = new CornerRadius(NativeTokens.RadiusS);

        // Two tones, the ring and a hairline of the card inside it, so it is seen on a plane and on the accent alike.
        _inner = new Border { BorderThickness = new Thickness(NativeTokens.Hairline), CornerRadius = CornerRadius };
        Child = _inner;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The ring lives in the window's adorner layer, so the colour and the corners are read off the control it rings:
        // a card in its own variant scope gets its own scope's ring.
        if (AdornerLayer.GetAdornedElement(this) is not Control adorned)
        {
            return;
        }

        BorderBrush = adorned.TryFindResource(BrushKey, adorned.ActualThemeVariant, out var brush) && brush is IBrush b
            ? b
            : new SolidColorBrush(NativeTokens.Dark.FocusRingInk);
        _inner.BorderBrush = adorned.TryFindResource(InnerKey, adorned.ActualThemeVariant, out var inner) && inner is IBrush i
            ? i
            : new SolidColorBrush(NativeTokens.Dark.Card);
        var corner = adorned switch
        {
            TemplatedControl t => t.CornerRadius,
            Border border => border.CornerRadius,
            _ => default,
        };
        if (corner != default)
        {
            CornerRadius = corner;
            var less = NativeTokens.FocusRingWidth;
            _inner.CornerRadius = new CornerRadius(
                Math.Max(0, corner.TopLeft - less), Math.Max(0, corner.TopRight - less),
                Math.Max(0, corner.BottomRight - less), Math.Max(0, corner.BottomLeft - less));
        }
    }
}

/// <summary>The style that gives every control in a scope the <see cref="FocusRing"/>.</summary>
internal sealed class FocusRingStyle : Style
{
    public FocusRingStyle()
        : base(x => x.Is<Control>())
    {
        Setters.Add(new Setter(Control.FocusAdornerProperty, new FuncTemplate<Control>(() => new FocusRing())));
    }
}

internal sealed partial record NativeTokens
{
    /// <summary>
    /// The floor the focus ring is held to on every plane: WCAG's non-text contrast (1.4.11), which a ring must clear to
    /// be seen. The mockup's focused edge (<c>#3a4f73</c>) is a field's border, about 2:1 on its card, and is lifted.
    /// </summary>
    public const double FocusRingFloor = 3.0;

    /// <summary>How wide the focus ring is drawn.</summary>
    public const double FocusRingWidth = 2;


    /// <summary>The focus ring's colour: <see cref="Focus"/> moved, as little as it must, to clear the floor on every plane.</summary>
    public Color FocusRingInk
    {
        get
        {
            var ring = ToRgb(Focus);
            Rgb[] planes = [ToRgb(Card), ToRgb(Well), ToRgb(Selected), ToRgb(KeyFill)];
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var plane in planes)
                {
                    ring = Contrast.Legible(ring, plane, FocusRingFloor);
                }
            }

            return Color.FromRgb(ring.R, ring.G, ring.B);
        }
    }

    /// <summary>
    /// Gives <paramref name="scope"/> the focus ring in these tokens' colour: every control in it rings when the keyboard
    /// reaches it. Called wherever the tokens are applied to a surface, and again when they change.
    /// </summary>
    public void ApplyFocusRing(StyledElement scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        NamedParts.Register();
        scope.Resources[global::SharpMUTerm.Avalonia.FocusRing.BrushKey] = Brush(FocusRingInk);
        scope.Resources[global::SharpMUTerm.Avalonia.FocusRing.InnerKey] = Brush(Card);
        if (!scope.Styles.OfType<FocusRingStyle>().Any())
        {
            scope.Styles.Add(new FocusRingStyle());
        }
    }

    private static Rgb ToRgb(Color colour) => new(colour.R, colour.G, colour.B);
}

/// <summary>
/// A named control's own parts take its name: a number box's text field and spinner, an editable combo box's text field.
/// The keyboard lands on the part, not on the control the view named, so without this a screen reader says "edit" and
/// nothing else on every number and route field of the settings window.
/// </summary>
internal static class NamedParts
{
    private static int _registered;

    /// <summary>Registers the class handler, once per process.</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            TemplatedControl.TemplateAppliedEvent.AddClassHandler<TemplatedControl>((owner, _) => Name(owner));
        }
    }

    // A part is presented after its owner's template is applied (a number box's text field sits in a content control's
    // content), so each part takes the name when its own template is applied, from the control whose template made it.
    private static void Name(TemplatedControl part)
    {
        if (!part.Focusable
            || !string.IsNullOrEmpty(global::Avalonia.Automation.AutomationProperties.GetName(part))
            || part.TemplatedParent is not Control owner
            || global::Avalonia.Automation.AutomationProperties.GetName(owner) is not { Length: > 0 } name)
        {
            return;
        }

        global::Avalonia.Automation.AutomationProperties.SetName(part, part.Name switch
        {
            "PART_IncreaseButton" => $"Increase {name}",
            "PART_DecreaseButton" => $"Decrease {name}",
            _ => name,
        });
    }
}
