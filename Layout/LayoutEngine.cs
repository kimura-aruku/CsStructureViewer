using System.Windows;
using CsStructureViewer.Models;
using CsStructureViewer.Settings;

namespace CsStructureViewer.Layout;

internal enum Side { Left, Right, Top, Bottom }

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
    private const double RoutingMargin = 18.0;

    public LayoutResult Calculate(ProjectGraph graph, AppSettings settings, double canvasMaxWidth)
    {
        var result = new LayoutResult();
        var sizes = new Dictionary<ClassNode, Size>();

        // Identify folder namespaces
        foreach (var ns in graph.Namespaces)
        {
            if (settings.InternalExcludePatterns.Any(p => MatchesNamespacePattern(ns.Name, p)))
                result.FolderNamespaces.Add(ns);
        }

        // Step 1: Calculate sizes for all classes (including nested, bottom-up)
        foreach (var node in EnumerateAllTopLevel(graph))
            CalcSize(node, sizes);

        // Step 2 & 3: Place classes within namespaces, compute namespace rects
        double x = 0, y = 0, rowH = 0;
        foreach (var ns in graph.Namespaces)
        {
            var nsSize = CalcNamespaceLayout(ns, sizes, result.ClassRects, originX: 0, originY: 0);

            if (x > 0 && x + nsSize.Width > canvasMaxWidth)
            {
                x = 0;
                y += rowH + NsGap;
                rowH = 0;
            }

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

        // Step 6: Arrow routes with orthogonal routing + spread
        var eligibleEdges = graph.Edges
            .Where(e => !IsInFolderNamespace(e.Source, result.FolderNamespaces) &&
                        !IsInFolderNamespace(e.Target, result.FolderNamespaces) &&
                        result.ClassRects.ContainsKey(e.Source) &&
                        result.ClassRects.ContainsKey(e.Target))
            .ToList();

        RouteArrows(eligibleEdges, result.ClassRects, result);

        return result;
    }

    // ── Pattern matching ────────────────────────────────────────────

    private static bool MatchesNamespacePattern(string namespaceName, string pattern) =>
        !string.IsNullOrEmpty(pattern) &&
        (namespaceName.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
         namespaceName.StartsWith(pattern + ".", StringComparison.OrdinalIgnoreCase));

    private static bool IsInFolderNamespace(ClassNode cls, HashSet<NamespaceNode> folderNamespaces) =>
        folderNamespaces.Any(fn => fn.Classes.Contains(cls));

    // ── Arrow routing ────────────────────────────────────────────────

    private static void RouteArrows(
        List<DependencyEdge> edges,
        Dictionary<ClassNode, Rect> classRects,
        LayoutResult result)
    {
        if (edges.Count == 0) return;

        var sideInfo = edges
            .Select(e => DetermineNaturalSides(classRects[e.Source], classRects[e.Target]))
            .ToArray();

        // Group by (node, side) → list of (edgeIndex, isSrc)
        var groups = new Dictionary<(ClassNode node, Side side), List<(int idx, bool isSrc)>>();
        for (int i = 0; i < edges.Count; i++)
        {
            var (srcSide, tgtSide) = sideInfo[i];
            AddToGroup(groups, (edges[i].Source, srcSide), (i, true));
            AddToGroup(groups, (edges[i].Target, tgtSide), (i, false));
        }

        // Assign spread fractions within each group
        var srcFractions = new double[edges.Count];
        var tgtFractions = new double[edges.Count];
        foreach (var list in groups.Values)
        {
            for (int j = 0; j < list.Count; j++)
            {
                double frac = (j + 1.0) / (list.Count + 1.0);
                var (idx, isSrc) = list[j];
                if (isSrc) srcFractions[idx] = frac;
                else tgtFractions[idx] = frac;
            }
        }

        // Route each edge
        for (int i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            var srcRect = classRects[edge.Source];
            var tgtRect = classRects[edge.Target];
            var (srcSide, tgtSide) = sideInfo[i];

            var srcPt = AttachPoint(srcRect, srcSide, srcFractions[i]);
            var tgtPt = AttachPoint(tgtRect, tgtSide, tgtFractions[i]);

            var waypoints = RouteOrthogonal(srcPt, srcSide, tgtPt, tgtSide, srcRect, tgtRect);
            result.Arrows.Add(new ArrowRoute(waypoints, edge.Kind));
        }
    }

    private static void AddToGroup<TKey, TVal>(
        Dictionary<TKey, List<TVal>> dict, TKey key, TVal val) where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<TVal>();
            dict[key] = list;
        }
        list.Add(val);
    }

    private static (Side srcSide, Side tgtSide) DetermineNaturalSides(Rect srcRect, Rect tgtRect)
    {
        var dx = Center(tgtRect).X - Center(srcRect).X;
        var dy = Center(tgtRect).Y - Center(srcRect).Y;
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx >= 0 ? (Side.Right, Side.Left) : (Side.Left, Side.Right);
        return dy >= 0 ? (Side.Bottom, Side.Top) : (Side.Top, Side.Bottom);
    }

    private static Point AttachPoint(Rect rect, Side side, double fraction) => side switch
    {
        Side.Left => new Point(rect.Left, rect.Top + rect.Height * fraction),
        Side.Right => new Point(rect.Right, rect.Top + rect.Height * fraction),
        Side.Top => new Point(rect.Left + rect.Width * fraction, rect.Top),
        Side.Bottom => new Point(rect.Left + rect.Width * fraction, rect.Bottom),
        _ => Center(rect)
    };

    private static List<Point> RouteOrthogonal(
        Point srcPt, Side srcSide, Point tgtPt, Side tgtSide, Rect srcRect, Rect tgtRect)
    {
        var pts = new List<Point> { srcPt };
        var exitPt = Extend(srcPt, srcSide, RoutingMargin);
        var entryPt = Extend(tgtPt, tgtSide, RoutingMargin);

        bool srcH = srcSide is Side.Left or Side.Right;
        bool tgtH = tgtSide is Side.Left or Side.Right;

        if (srcH && tgtH)
        {
            if (srcSide == tgtSide)
            {
                // U-shape (same horizontal side)
                double extreme = srcSide == Side.Right
                    ? Math.Max(srcRect.Right, tgtRect.Right) + 30
                    : Math.Min(srcRect.Left, tgtRect.Left) - 30;
                pts.Add(exitPt);
                pts.Add(new Point(extreme, exitPt.Y));
                pts.Add(new Point(extreme, entryPt.Y));
                pts.Add(entryPt);
            }
            else
            {
                // Z-shape (opposite horizontal sides)
                double midX = (exitPt.X + entryPt.X) / 2;
                pts.Add(exitPt);
                if (Math.Abs(exitPt.Y - entryPt.Y) < 1)
                {
                    pts.Add(entryPt);
                }
                else
                {
                    pts.Add(new Point(midX, exitPt.Y));
                    pts.Add(new Point(midX, entryPt.Y));
                    pts.Add(entryPt);
                }
            }
        }
        else if (!srcH && !tgtH)
        {
            if (srcSide == tgtSide)
            {
                // U-shape (same vertical side)
                double extreme = srcSide == Side.Bottom
                    ? Math.Max(srcRect.Bottom, tgtRect.Bottom) + 30
                    : Math.Min(srcRect.Top, tgtRect.Top) - 30;
                pts.Add(exitPt);
                pts.Add(new Point(exitPt.X, extreme));
                pts.Add(new Point(entryPt.X, extreme));
                pts.Add(entryPt);
            }
            else
            {
                // Z-shape (opposite vertical sides)
                double midY = (exitPt.Y + entryPt.Y) / 2;
                pts.Add(exitPt);
                if (Math.Abs(exitPt.X - entryPt.X) < 1)
                {
                    pts.Add(entryPt);
                }
                else
                {
                    pts.Add(new Point(exitPt.X, midY));
                    pts.Add(new Point(entryPt.X, midY));
                    pts.Add(entryPt);
                }
            }
        }
        else if (srcH)
        {
            // Source horizontal, target vertical: L-shape
            pts.Add(exitPt);
            pts.Add(new Point(entryPt.X, exitPt.Y));
            pts.Add(entryPt);
        }
        else
        {
            // Source vertical, target horizontal: L-shape
            pts.Add(exitPt);
            pts.Add(new Point(exitPt.X, entryPt.Y));
            pts.Add(entryPt);
        }

        pts.Add(tgtPt);
        return pts;
    }

    private static Point Extend(Point pt, Side side, double margin) => side switch
    {
        Side.Right => new Point(pt.X + margin, pt.Y),
        Side.Left => new Point(pt.X - margin, pt.Y),
        Side.Bottom => new Point(pt.X, pt.Y + margin),
        Side.Top => new Point(pt.X, pt.Y - margin),
        _ => pt
    };

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

    // ── Helpers ─────────────────────────────────────────────────────

    private static Point Center(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private static IEnumerable<ClassNode> EnumerateAllTopLevel(ProjectGraph graph)
    {
        foreach (var ns in graph.Namespaces)
            foreach (var cls in ns.Classes)
                yield return cls;
        foreach (var cls in graph.GlobalClasses)
            yield return cls;
    }
}
