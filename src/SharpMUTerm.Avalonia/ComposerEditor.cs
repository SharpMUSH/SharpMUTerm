using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using SharpMUTerm.Client.Composer;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The composer's text: a native <see cref="TextBox"/> — wrapping, undo and redo, selection, the platform's IME and
/// paste — that draws a wavy underline under each word the dictionary does not know. The underline is drawn over the
/// text from the box's own layout (<see cref="TextPresenter.TextLayout"/>), so it follows wrapping, scrolling and the
/// face exactly; the ranges are <see cref="ComposerSession.Marks"/>, the function the client marks its cells by.
/// <para>
/// Lines are broken by <c>\n</c> whatever the platform (<see cref="TextBox.NewLine"/>), so an offset into
/// <see cref="TextBox.Text"/> is an offset into the client's buffer and a mark lands on the word it names.
/// </para>
/// </summary>
internal sealed class ComposerEditor : TextBox
{
    private TextPresenter? _presenter;
    private ScrollViewer? _scroller;
    private IReadOnlyList<SpellRange> _marks = [];

    public ComposerEditor()
    {
        AcceptsReturn = true;
        AcceptsTab = false;
        TextWrapping = TextWrapping.Wrap;
        NewLine = "\n";
        BorderThickness = new Thickness(0);
        Padding = new Thickness(12, 10);
        MinHeight = 0;
        ClipToBounds = true;
    }

    /// <summary>The TextBox's own look: this is a text box, not a new control to style.</summary>
    protected override Type StyleKeyOverride => typeof(TextBox);

    /// <summary>The wavy line's colour.</summary>
    public Color MarkColour { get; set; } = Color.FromRgb(0xff, 0x6b, 0x6b);

    /// <summary>The words underlined now, as ranges of <see cref="TextBox.Text"/>.</summary>
    public IReadOnlyList<SpellRange> Marks
    {
        get => _marks;
        set
        {
            if (!_marks.SequenceEqual(value))
            {
                _marks = value;
                MarksMoved?.Invoke();
            }
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_scroller is not null)
        {
            _scroller.ScrollChanged -= OnScrolled;
        }

        _presenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
        _scroller = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        if (_scroller is not null)
        {
            _scroller.ScrollChanged += OnScrolled;
        }

        if (_presenter is not null)
        {
            _presenter.LayoutUpdated += (_, _) => MarksMoved?.Invoke();
        }
    }

    private void OnScrolled(object? sender, ScrollChangedEventArgs e) => MarksMoved?.Invoke();

    /// <summary>Raised when the marks, or where the text lies, changed: the layer drawing the lines repaints.</summary>
    public event Action? MarksMoved;

    /// <summary>The box's own text layout, for a test and the context menu; null before the template is applied.</summary>
    internal TextPresenter? Presenter => _presenter;

    /// <summary>The offset into <see cref="TextBox.Text"/> at a point in this control's coordinates, or null.</summary>
    internal int? OffsetAt(Point point)
    {
        if (_presenter is not { TextLayout: { } layout } presenter || this.TranslatePoint(point, presenter) is not { } at)
        {
            return null;
        }

        var hit = layout.HitTestPoint(at);
        return Math.Clamp(hit.TextPosition + (hit.IsTrailing ? 1 : 0), 0, (Text ?? string.Empty).Length);
    }

    /// <summary>
    /// The rectangles each mark covers, in this control's coordinates, clipped to what is in view: one per line a
    /// wrapped word lands on. Empty before the template is applied.
    /// </summary>
    public IReadOnlyList<Rect> MarkRects()
    {
        if (_presenter is not { } presenter || _marks.Count == 0 || presenter.TextLayout is not { } layout)
        {
            return [];
        }

        var text = Text ?? string.Empty;
        var view = new Rect(Bounds.Size);
        var rects = new List<Rect>();
        foreach (var mark in _marks)
        {
            if (mark.Start < 0 || mark.Start + mark.Length > text.Length || mark.Length <= 0)
            {
                continue;
            }

            foreach (var hit in layout.HitTestTextRange(mark.Start, mark.Length))
            {
                if (presenter.TranslatePoint(hit.TopLeft, this) is { } at)
                {
                    var rect = new Rect(at, hit.Size);
                    if (rect.Intersects(view))
                    {
                        rects.Add(rect);
                    }
                }
            }
        }

        return rects;
    }

    /// <summary>
    /// Draws the wavy lines onto <paramref name="surface"/>. A <see cref="TextBox"/> paints its text and its own
    /// background in children of its template, which are drawn after anything the box itself draws, so the lines are
    /// drawn by a layer lying over the editor (<see cref="ComposerView.MarkLayer"/>), which calls this.
    /// </summary>
    public void DrawMarks(DrawingContext context, Visual surface)
    {
        var rects = MarkRects();
        if (rects.Count == 0)
        {
            return;
        }

        var pen = new Pen(new SolidColorBrush(MarkColour), 1.2, lineCap: PenLineCap.Round);
        foreach (var rect in rects)
        {
            if (this.TranslatePoint(rect.BottomLeft, surface) is not { } origin)
            {
                continue;
            }

            var y = origin.Y - 1.5;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                const double step = 2.0;
                var x = origin.X;
                var end = origin.X + rect.Width;
                g.BeginFigure(new Point(x, y), false);
                var up = true;
                while (x < end)
                {
                    x = Math.Min(end, x + step);
                    g.LineTo(new Point(x, up ? y - 1.6 : y));
                    up = !up;
                }

                g.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }
    }
}
