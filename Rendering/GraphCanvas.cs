using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CsStructureViewer.Helpers;
using CsStructureViewer.Layout;
using CsStructureViewer.Models;
using WpfLineSegment = System.Windows.Media.LineSegment;

namespace CsStructureViewer.Rendering;

public class GraphCanvas : FrameworkElement
{
    public static readonly DependencyProperty LayoutResultProperty =
        DependencyProperty.Register(nameof(LayoutResult), typeof(LayoutResult), typeof(GraphCanvas),
            new PropertyMetadata(null, (d, e) => ((GraphCanvas)d).Render((LayoutResult?)e.NewValue)));

    public LayoutResult? LayoutResult
    {
        get => (LayoutResult?)GetValue(LayoutResultProperty);
        set => SetValue(LayoutResultProperty, value);
    }

    private readonly Canvas _outer;
    private readonly Canvas _inner;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translate = new(0, 0);
    private bool _isPanning;
    private Point _panStart;
    private Vector _translateOrigin;

    private static readonly Color GlobalClassColor = Color.FromRgb(180, 185, 195);
    private static readonly Color FolderFillColor = Color.FromArgb(45, 140, 140, 140);
    private static readonly Color FolderBorderColor = Color.FromArgb(180, 110, 110, 110);

    public GraphCanvas()
    {
        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_translate);

        _inner = new Canvas { RenderTransform = group };
        _outer = new Canvas { ClipToBounds = true, Background = Brushes.Transparent };
        _outer.Children.Add(_inner);

        AddVisualChild(_outer);
        AddLogicalChild(_outer);

        _outer.MouseWheel += OnMouseWheel;
        _outer.MouseLeftButtonDown += OnMouseDown;
        _outer.MouseLeftButtonUp += OnMouseUp;
        _outer.MouseMove += OnMouseMove;
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _outer;

    protected override Size MeasureOverride(Size availableSize)
    {
        _outer.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _outer.Arrange(new Rect(finalSize));
        return finalSize;
    }

    // ── Render ───────────────────────────────────────────────────────

    private void Render(LayoutResult? result)
    {
        _inner.Children.Clear();
        if (result == null) return;

        var total = result.NamespaceOrder.Count;

        // Layer 1: folder rects (below everything)
        foreach (var ns in result.FolderNamespaces)
        {
            if (result.NamespaceRects.TryGetValue(ns, out var rect))
                DrawFolderRect(ns, rect);
        }

        // Layer 2: regular namespace rects
        for (var i = 0; i < total; i++)
        {
            var ns = result.NamespaceOrder[i];
            if (result.FolderNamespaces.Contains(ns)) continue;
            var (nsColor, _) = ColorPalette.GetColors(i, total);
            DrawNamespaceRect(ns, result.NamespaceRects[ns], nsColor);
        }

        // Layer 3: arrows
        foreach (var arrow in result.Arrows)
            DrawArrow(arrow);

        // Layer 4: class rects (namespace classes, skip folder namespaces)
        for (var i = 0; i < total; i++)
        {
            var ns = result.NamespaceOrder[i];
            if (result.FolderNamespaces.Contains(ns)) continue;
            var (_, classColor) = ColorPalette.GetColors(i, total);
            foreach (var cls in ns.Classes)
                DrawClassRect(cls, result.ClassRects, classColor);
        }

        // Layer 4: class rects (global classes)
        foreach (var cls in result.GlobalClasses)
            DrawClassRect(cls, result.ClassRects, GlobalClassColor);
    }

    // ── Folder rect drawing ──────────────────────────────────────────

    private void DrawFolderRect(NamespaceNode ns, Rect rect)
    {
        var bg = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Fill = new SolidColorBrush(FolderFillColor),
            Stroke = new SolidColorBrush(FolderBorderColor),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection([5, 3]),
            RadiusX = 4,
            RadiusY = 4
        };
        Canvas.SetLeft(bg, rect.X);
        Canvas.SetTop(bg, rect.Y);
        _inner.Children.Add(bg);

        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 220, 220, 220)),
            BorderBrush = new SolidColorBrush(FolderBorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1, 6, 1),
            Child = new TextBlock
            {
                Text = ns.Name,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DimGray
            }
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, rect.X + 12);
        Canvas.SetTop(label, rect.Y - label.DesiredSize.Height / 2);
        _inner.Children.Add(label);
    }

    // ── Namespace drawing ────────────────────────────────────────────

    private void DrawNamespaceRect(NamespaceNode ns, Rect rect, Color color)
    {
        var solidColor = Color.FromArgb(255, color.R, color.G, color.B);

        var border = new Border
        {
            Width = rect.Width,
            Height = rect.Height,
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(solidColor),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(4)
        };
        Canvas.SetLeft(border, rect.X);
        Canvas.SetTop(border, rect.Y);
        _inner.Children.Add(border);

        // Label overlapping the top border
        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(solidColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1, 6, 1),
            Child = new TextBlock
            {
                Text = ns.Name,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DarkSlateGray
            }
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, rect.X + 12);
        Canvas.SetTop(label, rect.Y - label.DesiredSize.Height / 2);
        _inner.Children.Add(label);
    }

    // ── Class drawing ────────────────────────────────────────────────

    private void DrawClassRect(
        ClassNode cls, Dictionary<ClassNode, Rect> rects, Color color)
    {
        if (!rects.TryGetValue(cls, out var rect)) return;

        var borderColor = DarkenColor(color, 0.25);
        var border = new Border
        {
            Width = rect.Width,
            Height = rect.Height,
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = new TextBlock
            {
                Text = cls.Name,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = Brushes.Black,
                Padding = new Thickness(4, 4, 4, 0)
            }
        };
        Canvas.SetLeft(border, rect.X);
        Canvas.SetTop(border, rect.Y);
        Panel.SetZIndex(border, 10);
        _inner.Children.Add(border);

    }

    // ── Arrow drawing ────────────────────────────────────────────────

    private void DrawArrow(ArrowRoute arrow)
    {
        if (arrow.Segments.Count == 0) return;

        var lastSeg = arrow.Segments[^1];
        var end = arrow.End;
        double angle = lastSeg.Direction switch
        {
            Direction.Right => 0.0,
            Direction.Down  => Math.PI / 2,
            Direction.Left  => Math.PI,
            Direction.Up    => -Math.PI / 2,
            _ => 0.0
        };

        const double arrowLen = 13.0;
        const double halfAngle = Math.PI / 6;

        var isTriangle = arrow.Kind != DependencyKind.FieldReference;
        var lineEnd = isTriangle
            ? new Point(end.X - arrowLen * Math.Cos(angle),
                        end.Y - arrowLen * Math.Sin(angle))
            : end;

        // Shaft: Start → intermediate ends → lineEnd
        var fig = new PathFigure { StartPoint = arrow.Start };
        for (int i = 0; i < arrow.Segments.Count - 1; i++)
            fig.Segments.Add(new WpfLineSegment(arrow.Segments[i].End, isStroked: true));
        fig.Segments.Add(new WpfLineSegment(lineEnd, isStroked: true));

        var geom = new PathGeometry();
        geom.Figures.Add(fig);

        var shaft = new Path
        {
            Data = geom,
            Stroke = Brushes.DimGray,
            StrokeThickness = 1.5
        };
        if (arrow.Kind == DependencyKind.Implementation)
            shaft.StrokeDashArray = new DoubleCollection([5, 3]);
        Panel.SetZIndex(shaft, 5);
        _inner.Children.Add(shaft);

        // Arrowhead
        var left  = new Point(end.X - arrowLen * Math.Cos(angle - halfAngle),
                              end.Y - arrowLen * Math.Sin(angle - halfAngle));
        var right = new Point(end.X - arrowLen * Math.Cos(angle + halfAngle),
                              end.Y - arrowLen * Math.Sin(angle + halfAngle));

        var headGeom = new PathGeometry();
        if (isTriangle)
        {
            var hf = new PathFigure { StartPoint = left, IsClosed = true };
            hf.Segments.Add(new WpfLineSegment(end, isStroked: true));
            hf.Segments.Add(new WpfLineSegment(right, isStroked: true));
            headGeom.Figures.Add(hf);
        }
        else
        {
            var f1 = new PathFigure { StartPoint = left };
            f1.Segments.Add(new WpfLineSegment(end, isStroked: true));
            var f2 = new PathFigure { StartPoint = right };
            f2.Segments.Add(new WpfLineSegment(end, isStroked: true));
            headGeom.Figures.Add(f1);
            headGeom.Figures.Add(f2);
        }

        var head = new Path
        {
            Data = headGeom,
            Stroke = Brushes.DimGray,
            StrokeThickness = 1.5,
            Fill = isTriangle ? Brushes.White : Brushes.Transparent
        };
        Panel.SetZIndex(head, 15);
        _inner.Children.Add(head);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static Color DarkenColor(Color c, double amount) =>
        Color.FromArgb(c.A,
            (byte)Math.Max(0, c.R - (int)(255 * amount)),
            (byte)Math.Max(0, c.G - (int)(255 * amount)),
            (byte)Math.Max(0, c.B - (int)(255 * amount)));

    // ── Zoom ─────────────────────────────────────────────────────────

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        var mouse = e.GetPosition(_outer);
        var newScale = Math.Clamp(_scale.ScaleX * factor, 0.05, 20.0);
        factor = newScale / _scale.ScaleX;

        _translate.X = mouse.X * (1 - factor) + _translate.X * factor;
        _translate.Y = mouse.Y * (1 - factor) + _translate.Y * factor;
        _scale.ScaleX = newScale;
        _scale.ScaleY = newScale;
    }

    // ── Pan ──────────────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _panStart = e.GetPosition(_outer);
        _translateOrigin = new Vector(_translate.X, _translate.Y);
        _outer.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var pos = e.GetPosition(_outer);
        _translate.X = _translateOrigin.X + (pos.X - _panStart.X);
        _translate.Y = _translateOrigin.Y + (pos.Y - _panStart.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        _outer.ReleaseMouseCapture();
    }
}
