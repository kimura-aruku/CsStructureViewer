using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CsStructureViewer.Helpers;
using CsStructureViewer.Diagnostics;
using CsStructureViewer.Layout;
using CsStructureViewer.Models;
using WpfLineSegment = System.Windows.Media.LineSegment;

namespace CsStructureViewer.Rendering;

public class GraphCanvas : FrameworkElement
{
    private const double ContentMargin = 48.0;
    private const double InitialViewportPadding = 24.0;

    public static readonly DependencyProperty LayoutResultProperty =
        DependencyProperty.Register(nameof(LayoutResult), typeof(LayoutResult), typeof(GraphCanvas),
            new PropertyMetadata(null, (d, e) => ((GraphCanvas)d).Render((LayoutResult?)e.NewValue, resetViewport: true)));

    public static readonly DependencyProperty DebugClassTransparencyEnabledProperty =
        DependencyProperty.Register(nameof(DebugClassTransparencyEnabled), typeof(bool), typeof(GraphCanvas),
            new PropertyMetadata(false, (d, _) => ((GraphCanvas)d).Render(((GraphCanvas)d).LayoutResult)));

    public LayoutResult? LayoutResult
    {
        get => (LayoutResult?)GetValue(LayoutResultProperty);
        set => SetValue(LayoutResultProperty, value);
    }

    public bool DebugClassTransparencyEnabled
    {
        get => (bool)GetValue(DebugClassTransparencyEnabledProperty);
        set => SetValue(DebugClassTransparencyEnabledProperty, value);
    }

    private readonly Canvas _outer;
    private readonly Canvas _inner;
    private readonly TranslateTransform _contentTranslate = new(0, 0);
    private readonly ScaleTransform _scale = new(1, 1);
    private bool _isPanning;
    private Point _panStart;
    private Point _scrollOrigin;
    private string? _transparentClassKey;
    private Size _baseContentSize = new(1, 1);
    private Rect _centralBounds = Rect.Empty;

    public event EventHandler? InitialViewportRequested;

    private static readonly Color GlobalClassColor = Color.FromRgb(180, 185, 195);
    private static readonly Color FolderFillColor = Color.FromArgb(45, 140, 140, 140);
    private static readonly Color FolderBorderColor = Color.FromArgb(180, 110, 110, 110);

    public GraphCanvas()
    {
        var group = new TransformGroup();
        group.Children.Add(_contentTranslate);
        group.Children.Add(_scale);

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
        var contentSize = ScaledContentSize;
        _outer.Measure(contentSize);
        return contentSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var contentSize = ScaledContentSize;
        var arrangeSize = new Size(
            Math.Max(finalSize.Width, contentSize.Width),
            Math.Max(finalSize.Height, contentSize.Height));
        _outer.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }

    public Point GetInitialScrollOffset(Size viewportSize)
    {
        if (_centralBounds.IsEmpty)
            return new Point(0, 0);

        var contentSize = ScaledContentSize;
        var x = Math.Max(0, _centralBounds.X * _scale.ScaleX - InitialViewportPadding);
        var y = Math.Max(0, _centralBounds.Y * _scale.ScaleY - InitialViewportPadding);

        if (!double.IsNaN(viewportSize.Width) && viewportSize.Width > 0)
            x = Math.Min(x, Math.Max(0, contentSize.Width - viewportSize.Width));
        if (!double.IsNaN(viewportSize.Height) && viewportSize.Height > 0)
            y = Math.Min(y, Math.Max(0, contentSize.Height - viewportSize.Height));

        return new Point(x, y);
    }

    private Size ScaledContentSize =>
        new(
            Math.Max(1, _baseContentSize.Width * _scale.ScaleX),
            Math.Max(1, _baseContentSize.Height * _scale.ScaleY));

    // ── Render ───────────────────────────────────────────────────────

    private void Render(LayoutResult? result, bool resetViewport = false)
    {
        _inner.Children.Clear();
        if (resetViewport)
            ResetViewTransform();
        UpdateLayoutMetrics(result);
        InvalidateMeasure();
        if (result == null) return;

        var total = result.NamespaceOrder.Count;
        var sourceColors = BuildSourceColors(result);

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
            DrawArrow(arrow, sourceColors.GetValueOrDefault(arrow.SourceKey, Colors.DimGray));

        // Layer 4: class rects (namespace classes, skip folder namespaces)
        for (var i = 0; i < total; i++)
        {
            if (result.DisplayMode == GraphDisplayMode.Namespace)
                break;

            var ns = result.NamespaceOrder[i];
            if (result.FolderNamespaces.Contains(ns)) continue;
            var (_, classColor) = ColorPalette.GetColors(i, total);
            foreach (var cls in ns.Classes)
                DrawClassRect(cls, result.ClassRects, classColor);
        }

        // Layer 4: class rects (global classes)
        if (result.DisplayMode == GraphDisplayMode.Class)
            foreach (var cls in result.GlobalClasses)
                DrawClassRect(cls, result.ClassRects, GlobalClassColor);

        if (!string.IsNullOrWhiteSpace(result.ProjectPath))
            LayoutDiagnosticsWriter.WriteLatest(result, result.ProjectPath);

        if (resetViewport)
            InitialViewportRequested?.Invoke(this, EventArgs.Empty);
    }

    private static Dictionary<string, Color> BuildSourceColors(LayoutResult result)
    {
        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        var total = result.NamespaceOrder.Count;

        for (var i = 0; i < total; i++)
        {
            var ns = result.NamespaceOrder[i];
            if (result.FolderNamespaces.Contains(ns))
            {
                colors[$"namespace:{ns.Name}"] = FolderBorderColor;
                continue;
            }

            var (_, classColor) = ColorPalette.GetColors(i, total);
            colors[$"namespace:{ns.Name}"] = classColor;
            foreach (var cls in ns.Classes)
                colors[$"class:{cls.FullyQualifiedName}"] = classColor;
        }

        foreach (var cls in result.GlobalClasses)
            colors[$"class:{cls.FullyQualifiedName}"] = GlobalClassColor;

        return colors;
    }

    private void ResetViewTransform()
    {
        _scale.ScaleX = 1.0;
        _scale.ScaleY = 1.0;
    }

    private void UpdateLayoutMetrics(LayoutResult? result)
    {
        if (result == null)
        {
            _baseContentSize = new Size(1, 1);
            _centralBounds = Rect.Empty;
            _contentTranslate.X = 0.0;
            _contentTranslate.Y = 0.0;
            return;
        }

        var contentBounds = Rect.Empty;
        var centralBounds = Rect.Empty;

        foreach (var rect in result.NamespaceRects.Values)
        {
            contentBounds.Union(rect);
            centralBounds.Union(rect);
        }

        foreach (var rect in result.ClassRects.Values)
        {
            contentBounds.Union(rect);
            centralBounds.Union(rect);
        }

        foreach (var arrow in result.Arrows)
        {
            contentBounds.Union(arrow.Start);
            contentBounds.Union(arrow.End);
            foreach (var segment in arrow.Segments)
            {
                contentBounds.Union(segment.Start);
                contentBounds.Union(segment.End);
            }
        }

        if (contentBounds.IsEmpty)
        {
            _baseContentSize = new Size(1, 1);
            _centralBounds = Rect.Empty;
            return;
        }

        _contentTranslate.X = ContentMargin - contentBounds.Left;
        _contentTranslate.Y = ContentMargin - contentBounds.Top;

        _baseContentSize = new Size(
            Math.Max(1, contentBounds.Width + ContentMargin * 2),
            Math.Max(1, contentBounds.Height + ContentMargin * 2));
        _centralBounds = centralBounds.IsEmpty
            ? new Rect(ContentMargin, ContentMargin, contentBounds.Width, contentBounds.Height)
            : NormalizeRect(centralBounds);
    }

    private Rect NormalizeRect(Rect rect) =>
        new(
            rect.X + _contentTranslate.X,
            rect.Y + _contentTranslate.Y,
            rect.Width,
            rect.Height);

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
        var classKey = cls.FullyQualifiedName;
        var border = new Border
        {
            Width = rect.Width,
            Height = rect.Height,
            Background = DebugClassTransparencyEnabled && _transparentClassKey == classKey
                ? Brushes.Transparent
                : new SolidColorBrush(color),
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
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (!DebugClassTransparencyEnabled) return;

            _transparentClassKey = _transparentClassKey == classKey ? null : classKey;
            Render(LayoutResult);
            e.Handled = true;
        };
        Canvas.SetLeft(border, rect.X);
        Canvas.SetTop(border, rect.Y);
        Panel.SetZIndex(border, 10);
        _inner.Children.Add(border);

    }

    // ── Arrow drawing ────────────────────────────────────────────────

    private void DrawArrow(ArrowRoute arrow, Color color)
    {
        if (arrow.Segments.Count == 0) return;
        var brush = new SolidColorBrush(DarkenColor(color, 0.2));

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

        arrow.RenderedShaftPoints.Clear();
        arrow.RenderedShaftPoints.Add(arrow.Start);
        for (int i = 0; i < arrow.Segments.Count - 1; i++)
            arrow.RenderedShaftPoints.Add(arrow.Segments[i].End);
        arrow.RenderedShaftPoints.Add(lineEnd);

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
            Stroke = brush,
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

        arrow.RenderedHeadPoints.Clear();
        arrow.RenderedHeadPoints.Add(left);
        arrow.RenderedHeadPoints.Add(end);
        arrow.RenderedHeadPoints.Add(right);

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
            Stroke = brush,
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
        var scrollViewer = FindScrollViewer();
        if (scrollViewer == null)
            return;

        var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        var mouse = e.GetPosition(scrollViewer);
        var oldScale = _scale.ScaleX;
        var newScale = Math.Clamp(_scale.ScaleX * factor, 0.05, 20.0);
        var contentX = (scrollViewer.HorizontalOffset + mouse.X) / oldScale;
        var contentY = (scrollViewer.VerticalOffset + mouse.Y) / oldScale;

        _scale.ScaleX = newScale;
        _scale.ScaleY = newScale;
        InvalidateMeasure();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            scrollViewer.ScrollToHorizontalOffset(contentX * newScale - mouse.X);
            scrollViewer.ScrollToVerticalOffset(contentY * newScale - mouse.Y);
        }));
        e.Handled = true;
    }

    // ── Pan ──────────────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var scrollViewer = FindScrollViewer();
        if (scrollViewer == null)
            return;

        _isPanning = true;
        _panStart = e.GetPosition(scrollViewer);
        _scrollOrigin = new Point(scrollViewer.HorizontalOffset, scrollViewer.VerticalOffset);
        _outer.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var scrollViewer = FindScrollViewer();
        if (scrollViewer == null)
            return;

        var pos = e.GetPosition(scrollViewer);
        scrollViewer.ScrollToHorizontalOffset(_scrollOrigin.X - (pos.X - _panStart.X));
        scrollViewer.ScrollToVerticalOffset(_scrollOrigin.Y - (pos.Y - _panStart.Y));
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        _outer.ReleaseMouseCapture();
        e.Handled = true;
    }

    private ScrollViewer? FindScrollViewer()
    {
        DependencyObject? current = this;
        while (current != null)
        {
            if (current is ScrollViewer scrollViewer)
                return scrollViewer;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
