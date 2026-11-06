using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SharpMUTerm.Core.Avatars;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The settings window's crop step for a character's or a world's picture: the picture under the circle the sidebar
/// draws it in, dragged to place and zoomed (wheel, slider, + and −, arrows to move), then <b>Use</b> (Enter) or
/// <b>Cancel</b> (Esc). It lies over the settings page with a scrim, so nothing behind it takes a press while it is up.
/// The numbers are <see cref="AvatarCropStage"/>'s; what it hands back is the <see cref="AvatarCrop"/> the client keeps
/// a copy of.
/// </summary>
internal sealed class AvatarCropView : Grid
{
    /// <summary>The circle's diameter and the stage round it, in pixels.</summary>
    internal const double Diameter = 272, StageSide = 320;

    private const double PanStep = 8, PanStepLarge = 32, ZoomStep = 0.25;

    private readonly TaskCompletionSource<AvatarCrop?> _done = new();
    private readonly Slider _zoom;
    private bool _syncing;

    public AvatarCropView(Bitmap picture, NativeTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(tokens);
        Background = tokens.Brush(tokens.Scrim);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(this, "Crop the picture");

        Stage = new CropStage(picture, tokens);
        _zoom = new Slider
        {
            Minimum = 1,
            Maximum = AvatarCropStage.MaxZoom,
            Value = 1,
            SmallChange = ZoomStep,
            LargeChange = 1,
            Width = StageSide - 96,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(_zoom, "Zoom");
        _zoom.ValueChanged += (_, e) =>
        {
            if (!_syncing)
            {
                Stage.Model.ZoomTo(e.NewValue);
                Stage.InvalidateVisual();
            }
        };
        Stage.Moved += SyncSlider;

        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space12, HorizontalAlignment = HorizontalAlignment.Center };
        zoomRow.Children.Add(new TextBlock { Text = "Zoom", FontSize = NativeTokens.Caption, Foreground = tokens.Brush(tokens.Dim), VerticalAlignment = VerticalAlignment.Center });
        zoomRow.Children.Add(_zoom);

        CancelButton = ModalParts.Button("Cancel", "Esc", tokens, () => Finish(null));
        UseButton = ModalParts.Button("Use this picture", "Enter", tokens, () => Finish(Stage.Model.Crop), accent: true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(CancelButton);
        buttons.Children.Add(UseButton);

        var body = new StackPanel { Spacing = NativeTokens.Space16, Width = StageSide };
        body.Children.Add(new TextBlock { Text = "Crop the picture", FontSize = NativeTokens.Heading, FontWeight = FontWeight.SemiBold, Foreground = tokens.Brush(tokens.Text) });
        body.Children.Add(new TextBlock
        {
            Text = "Drag it to place it in the circle, and zoom with the wheel or the slider. The sidebar and the header show what is inside the circle.",
            FontSize = NativeTokens.Caption,
            Foreground = tokens.Brush(tokens.Dim),
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(Stage);
        body.Children.Add(zoomRow);
        body.Children.Add(buttons);

        Children.Add(new Border
        {
            Child = body,
            Background = tokens.Brush(tokens.Card),
            BorderBrush = tokens.Brush(tokens.Edge),
            BorderThickness = new Thickness(NativeTokens.Hairline),
            CornerRadius = new CornerRadius(NativeTokens.RadiusL),
            Padding = new Thickness(NativeTokens.Space24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        KeyDown += OnKey;
        PointerPressed += (_, e) => e.Handled = true; // the scrim takes the press; nothing behind it does
    }

    /// <summary>The picture and its circle.</summary>
    internal CropStage Stage { get; }

    internal Button CancelButton { get; }

    internal Button UseButton { get; }

    /// <summary>The square chosen, or null when the step was cancelled.</summary>
    public Task<AvatarCrop?> Result => _done.Task;

    /// <summary>Ends the step with <paramref name="crop"/> (null: cancelled).</summary>
    internal void Finish(AvatarCrop? crop) => _done.TrySetResult(crop);

    private void SyncSlider()
    {
        _syncing = true;
        _zoom.Value = Stage.Model.Zoom;
        _syncing = false;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (ModalParts.FocusedButtonTakes(e))
        {
            return;
        }

        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? PanStepLarge : PanStep;
        switch (e.Key)
        {
            case Key.Escape:
                Finish(null);
                break;
            case Key.Enter:
                Finish(Stage.Model.Crop);
                break;
            case Key.Left when ReferenceEquals(e.Source, Stage):
                Stage.Pan(-step, 0);
                break;
            case Key.Right when ReferenceEquals(e.Source, Stage):
                Stage.Pan(step, 0);
                break;
            case Key.Up when ReferenceEquals(e.Source, Stage):
                Stage.Pan(0, -step);
                break;
            case Key.Down when ReferenceEquals(e.Source, Stage):
                Stage.Pan(0, step);
                break;
            case Key.OemPlus or Key.Add:
                Stage.ZoomBy(ZoomStep);
                break;
            case Key.OemMinus or Key.Subtract:
                Stage.ZoomBy(-ZoomStep);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// The picture drawn at the stage's zoom and offset, the stage outside the circle veiled, and the circle's edge. A drag
    /// moves the picture with the pointer, the wheel zooms, and the arrows move it when it has the keyboard.
    /// </summary>
    internal sealed class CropStage : Control
    {
        private readonly Bitmap _picture;
        private readonly IBrush _veil;
        private readonly IPen _ring;
        private Point? _dragFrom;

        public CropStage(Bitmap picture, NativeTokens tokens)
        {
            _picture = picture;
            Model = new AvatarCropStage(picture.PixelSize.Width, picture.PixelSize.Height, Diameter);
            _veil = tokens.Brush(tokens.Scrim);
            _ring = new Pen(tokens.Brush(tokens.Accent), NativeTokens.Space2);
            Width = StageSide;
            Height = StageSide;
            Focusable = true;
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.SizeAll);
            AutomationProperties.SetName(this, "Picture: drag or use the arrows to move it, the wheel or + and − to zoom");
        }

        public AvatarCropStage Model { get; }

        /// <summary>Raised when the zoom changed from here (wheel or keys), so the slider follows.</summary>
        public event Action? Moved;

        private static double Inset => (StageSide - Diameter) / 2;

        public void Pan(double dx, double dy)
        {
            Model.Pan(dx, dy);
            InvalidateVisual();
        }

        public void ZoomBy(double delta)
        {
            Model.ZoomTo(Model.Zoom + delta);
            InvalidateVisual();
            Moved?.Invoke();
        }

        public override void Render(DrawingContext context)
        {
            var image = new Rect(Inset + Model.Left, Inset + Model.Top, Model.ImageWidth, Model.ImageHeight);
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            {
                context.DrawImage(_picture, new Rect(_picture.Size), image);
            }

            var circle = new Rect(Inset, Inset, Diameter, Diameter);
            var veil = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(Bounds.Size)), new EllipseGeometry(circle));
            context.DrawGeometry(_veil, null, veil);
            context.DrawEllipse(null, _ring, circle.Center, Diameter / 2, Diameter / 2);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _dragFrom = e.GetPosition(this);
                e.Pointer.Capture(this);
                Focus();
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_dragFrom is { } from)
            {
                var at = e.GetPosition(this);
                Pan(at.X - from.X, at.Y - from.Y);
                _dragFrom = at;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _dragFrom = null;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            ZoomBy(e.Delta.Y * ZoomStep);
            e.Handled = true;
        }
    }
}
