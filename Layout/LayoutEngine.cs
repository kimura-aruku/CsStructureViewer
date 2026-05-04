using System.Windows;
using CsStructureViewer.Models;

namespace CsStructureViewer.Layout;

public class LayoutEngine
{
    private const double CharWidth = 7.5;
    private const double LineHeight = 18.0;
    private const double ClassPadH = 12.0;
    private const double ClassPadV = 8.0;
    private const double MaxTextWidth = 176.0;
    private const double MaxClassWidth = 200.0;
    private const double MinClassWidth = 60.0;
    private const double ClassGap = 10.0;
    private const double NsPadding = 16.0;
    private const double NsLabelHeight = 22.0;
    private const double NsGap = 20.0;
    private const double MaxNsWidth = 600.0;
    private const double CanvasMaxWidth = 1400.0;

    public LayoutResult Calculate(ProjectGraph graph)
    {
        var result = new LayoutResult();
        var sizes = new Dictionary<ClassNode, Size>();

        // Step 1: Calculate sizes for all classes (including nested, bottom-up)
        foreach (var node in EnumerateAllTopLevel(graph))
            CalcSize(node, sizes);

        // Step 2 & 3: Place classes within namespaces, compute namespace rects
        double x = 0, y = 0, rowH = 0;
        foreach (var ns in graph.Namespaces)
        {
            var nsSize = CalcNamespaceLayout(ns, sizes, result.ClassRects, originX: 0, originY: 0);

            if (x > 0 && x + nsSize.Width > CanvasMaxWidth)
            {
                x = 0;
                y += rowH + NsGap;
                rowH = 0;
            }

            // Re-place with actual origin
            CalcNamespaceLayout(ns, sizes, result.ClassRects, originX: x, originY: y);
            result.NamespaceRects[ns] = new Rect(x, y, nsSize.Width, nsSize.Height);
            result.NamespaceOrder.Add(ns);

            x += nsSize.Width + NsGap;
            rowH = Math.Max(rowH, nsSize.Height);
        }

        // Step 5: Global classes (after all namespace rows)
        double globalY = y + rowH + (rowH > 0 ? NsGap : 0);
        double gx = 0;
        foreach (var node in graph.GlobalClasses)
        {
            PlaceClass(node, gx, globalY, sizes, result.ClassRects);
            gx += sizes[node].Width + ClassGap;
        }
        result.GlobalClasses.AddRange(graph.GlobalClasses);

        // Step 6: Arrow routes
        foreach (var edge in graph.Edges)
        {
            if (!result.ClassRects.TryGetValue(edge.Source, out var src)) continue;
            if (!result.ClassRects.TryGetValue(edge.Target, out var tgt)) continue;
            var (start, end) = BorderPoints(src, tgt);
            result.Arrows.Add(new ArrowRoute(start, end, edge.Kind));
        }

        return result;
    }

    // ── Size calculation ────────────────────────────────────────────

    private static void CalcSize(ClassNode node, Dictionary<ClassNode, Size> sizes)
    {
        foreach (var nested in node.NestedClasses)
            CalcSize(nested, sizes);

        var textW = node.Name.Length * CharWidth;
        double lines = textW <= MaxTextWidth ? 1 : Math.Ceiling(textW / MaxTextWidth);
        double w = Math.Clamp(Math.Min(textW, MaxTextWidth) + ClassPadH * 2, MinClassWidth, MaxClassWidth);
        double h = lines * LineHeight + ClassPadV * 2;

        if (node.NestedClasses.Count > 0)
        {
            var nestedH = node.NestedClasses.Sum(n => sizes[n].Height + ClassGap);
            var nestedW = node.NestedClasses.Max(n => sizes[n].Width) + NsPadding * 2;
            h += nestedH + NsPadding;
            w = Math.Max(w, nestedW);
        }

        sizes[node] = new Size(w, h);
    }

    // ── Namespace layout ────────────────────────────────────────────

    private static Size CalcNamespaceLayout(
        NamespaceNode ns, Dictionary<ClassNode, Size> sizes,
        Dictionary<ClassNode, Rect> classRects, double originX, double originY)
    {
        double cx = NsPadding, cy = NsLabelHeight + NsPadding;
        double rowH = 0, maxRight = NsPadding;

        foreach (var cls in ns.Classes)
        {
            var sz = sizes[cls];
            if (cx > NsPadding && cx + sz.Width > MaxNsWidth - NsPadding)
            {
                cy += rowH + ClassGap;
                cx = NsPadding;
                rowH = 0;
            }

            PlaceClass(cls, originX + cx, originY + cy, sizes, classRects);
            cx += sz.Width + ClassGap;
            rowH = Math.Max(rowH, sz.Height);
            maxRight = Math.Max(maxRight, cx);
        }

        var nsW = Math.Max(maxRight + NsPadding, 120.0);
        var nsH = cy + rowH + NsPadding;
        return new Size(nsW, nsH);
    }

    // ── Class placement (absolute, recursive) ───────────────────────

    private static void PlaceClass(
        ClassNode node, double ax, double ay,
        Dictionary<ClassNode, Size> sizes, Dictionary<ClassNode, Rect> classRects)
    {
        var sz = sizes[node];
        classRects[node] = new Rect(ax, ay, sz.Width, sz.Height);

        double nestedY = ay + LineHeight + ClassPadV * 2;
        double nestedX = ax + NsPadding;
        foreach (var nested in node.NestedClasses)
        {
            PlaceClass(nested, nestedX, nestedY, sizes, classRects);
            nestedY += sizes[nested].Height + ClassGap;
        }
    }

    // ── Arrow route helpers ─────────────────────────────────────────

    private static (Point, Point) BorderPoints(Rect src, Rect tgt)
    {
        var sc = Center(src);
        var tc = Center(tgt);
        return (BorderPoint(src, sc, tc), BorderPoint(tgt, tc, sc));
    }

    private static Point BorderPoint(Rect rect, Point from, Point to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (dx == 0 && dy == 0) return from;
        var hw = rect.Width / 2;
        var hh = rect.Height / 2;
        var tx = dx != 0 ? hw / Math.Abs(dx) : double.MaxValue;
        var ty = dy != 0 ? hh / Math.Abs(dy) : double.MaxValue;
        var t = Math.Min(tx, ty);
        return new Point(from.X + dx * t, from.Y + dy * t);
    }

    private static Point Center(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    // ── Graph traversal ─────────────────────────────────────────────

    private static IEnumerable<ClassNode> EnumerateAllTopLevel(ProjectGraph graph)
    {
        foreach (var ns in graph.Namespaces)
            foreach (var cls in ns.Classes)
                yield return cls;
        foreach (var cls in graph.GlobalClasses)
            yield return cls;
    }
}
