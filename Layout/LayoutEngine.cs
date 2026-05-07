using System.Windows;
using CsStructureViewer.Models;

namespace CsStructureViewer.Layout;

internal enum Side { Top }

internal sealed record LayoutRow(
    NamespaceNode? Namespace,
    IReadOnlyList<ClassNode> Classes,
    bool IsFolder,
    string? FolderKey,
    double Height);

internal sealed record LaneRoutePlan(
    string SourceKey,
    string TargetKey,
    string SourceLabel,
    string TargetLabel,
    DependencyKind Kind,
    int SourceRow,
    int TargetRow,
    int SourceHorizontalLane,
    int TargetHorizontalLane,
    int SideLane);

public class LayoutEngine
{
    private const double CharWidth = 7.5;
    private const double LineHeight = 18.0;
    private const double ClassPadH = 12.0;
    private const double ClassPadV = 8.0;
    private const double MaxTextWidth = 176.0;
    private const double MaxClassWidth = 200.0;
    private const double MinClassWidth = 60.0;
    private const double ClassGap = 18.0;
    private const double NsPadding = 16.0;
    private const double NsLabelHeight = 22.0;
    private const double NsGap = 24.0;
    private const double LaneGap = 12.0;
    private const double TopConnectionLength = 10.0;
    private const double BottomLaneClearance = LaneGap;
    private const double SideLaneGap = 26.0;
    private const double CentralMinWidth = 260.0;
    private const double CentralMaxWidth = 760.0;
    private const double FolderMinWidth = 140.0;
    private const double FolderHeight = 54.0;

    public LayoutResult Calculate(ProjectGraph graph, double canvasMaxWidth)
    {
        var result = new LayoutResult();
        var sizes = new Dictionary<ClassNode, Size>();
        var classNamespaces = BuildClassNamespaceMap(graph);
        var visibleFolderNamespaces = GetVisibleFolderNamespaces(graph, classNamespaces);

        foreach (var ns in visibleFolderNamespaces)
            result.FolderNamespaces.Add(ns);

        foreach (var node in EnumerateAllTopLevel(graph))
            CalcSize(node, sizes);

        var rows = BuildRows(graph, visibleFolderNamespaces, sizes);
        var rowByKey = BuildRowByKey(rows);
        var plans = BuildLaneRoutePlans(graph.Edges, classNamespaces, visibleFolderNamespaces, rowByKey);
        var rowLaneCounts = CountRowLanes(rows.Count, plans);
        var sideLaneCount = plans.Count(plan => plan.SideLane >= 0);
        var centralWidth = CalculateCentralWidth(graph, visibleFolderNamespaces, sizes);
        var leftSideLaneCount = (sideLaneCount + 1) / 2;
        var centralX = leftSideLaneCount * SideLaneGap + NsPadding * 2;

        var rowRects = PlaceRows(
            graph,
            rows,
            rowLaneCounts,
            sizes,
            result,
            centralX,
            centralWidth);

        var rectByKey = BuildRectByKey(result);
        RouteArrows(plans, rowRects, rectByKey, centralX, centralWidth, result);

        return result;
    }

    private static Dictionary<ClassNode, NamespaceNode> BuildClassNamespaceMap(ProjectGraph graph)
    {
        var map = new Dictionary<ClassNode, NamespaceNode>();
        foreach (var ns in graph.Namespaces)
            foreach (var cls in ns.Classes)
                map[cls] = ns;
        return map;
    }

    private static HashSet<NamespaceNode> GetVisibleFolderNamespaces(
        ProjectGraph graph,
        Dictionary<ClassNode, NamespaceNode> classNamespaces)
    {
        var visible = new HashSet<NamespaceNode>();
        foreach (var edge in graph.Edges)
        {
            var sourceNs = classNamespaces.GetValueOrDefault(edge.Source);
            var targetNs = classNamespaces.GetValueOrDefault(edge.Target);
            var sourceInternal = sourceNs?.IsInternal == true;
            var targetInternal = targetNs?.IsInternal == true;

            if (sourceInternal == targetInternal) continue;
            if (sourceInternal && sourceNs != null) visible.Add(sourceNs);
            if (targetInternal && targetNs != null) visible.Add(targetNs);
        }
        return visible;
    }

    private static List<LayoutRow> BuildRows(
        ProjectGraph graph,
        HashSet<NamespaceNode> visibleFolderNamespaces,
        Dictionary<ClassNode, Size> sizes)
    {
        var rows = new List<LayoutRow>();
        foreach (var ns in graph.Namespaces.Where(ns => !ns.IsInternal || visibleFolderNamespaces.Contains(ns)))
        {
            if (ns.IsInternal)
            {
                rows.Add(new LayoutRow(ns, [], IsFolder: true, NamespaceKey(ns), FolderHeight));
                continue;
            }

            AddClassRows(rows, ns, ns.Classes, sizes);
        }

        AddClassRows(rows, null, graph.GlobalClasses, sizes);

        return rows;
    }

    private static void AddClassRows(
        List<LayoutRow> rows,
        NamespaceNode? ns,
        IReadOnlyList<ClassNode> classes,
        Dictionary<ClassNode, Size> sizes)
    {
        var current = new List<ClassNode>();
        var currentWidth = 0.0;
        var currentHeight = 0.0;

        foreach (var cls in classes)
        {
            var size = sizes[cls];
            var nextWidth = current.Count == 0
                ? size.Width
                : currentWidth + ClassGap + size.Width;

            if (current.Count > 0 && nextWidth > CentralMaxWidth - NsPadding * 2)
            {
                rows.Add(new LayoutRow(ns, current.ToList(), IsFolder: false, FolderKey: null, currentHeight));
                current.Clear();
                currentWidth = 0;
                currentHeight = 0;
                nextWidth = size.Width;
            }

            current.Add(cls);
            currentWidth = nextWidth;
            currentHeight = Math.Max(currentHeight, size.Height);
        }

        if (current.Count > 0)
            rows.Add(new LayoutRow(ns, current.ToList(), IsFolder: false, FolderKey: null, currentHeight));
    }

    private static Dictionary<string, int> BuildRowByKey(List<LayoutRow> rows)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.FolderKey != null)
                map[row.FolderKey] = i;

            foreach (var cls in row.Classes)
                map[ClassKey(cls)] = i;
        }
        return map;
    }

    private static List<LaneRoutePlan> BuildLaneRoutePlans(
        List<DependencyEdge> edges,
        Dictionary<ClassNode, NamespaceNode> classNamespaces,
        HashSet<NamespaceNode> visibleFolderNamespaces,
        Dictionary<string, int> rowByKey)
    {
        var plans = new List<LaneRoutePlan>();
        var seen = new HashSet<(string sourceKey, string targetKey, DependencyKind kind)>();
        var nextRowLane = new Dictionary<int, int>();
        var nextSideLane = 0;

        foreach (var edge in edges)
        {
            var sourceNs = classNamespaces.GetValueOrDefault(edge.Source);
            var targetNs = classNamespaces.GetValueOrDefault(edge.Target);
            var sourceInternal = sourceNs?.IsInternal == true;
            var targetInternal = targetNs?.IsInternal == true;

            if (sourceInternal && targetInternal)
                continue;

            if (sourceInternal && (sourceNs == null || !visibleFolderNamespaces.Contains(sourceNs)))
                continue;

            if (targetInternal && (targetNs == null || !visibleFolderNamespaces.Contains(targetNs)))
                continue;

            var sourceKey = sourceInternal ? NamespaceKey(sourceNs!) : ClassKey(edge.Source);
            var targetKey = targetInternal ? NamespaceKey(targetNs!) : ClassKey(edge.Target);
            if (!seen.Add((sourceKey, targetKey, edge.Kind)))
                continue;

            if (!rowByKey.TryGetValue(sourceKey, out var sourceRow) ||
                !rowByKey.TryGetValue(targetKey, out var targetRow))
                continue;

            var sourceLane = AllocateRowLane(nextRowLane, sourceRow);
            var targetLane = sourceRow == targetRow
                ? sourceLane
                : AllocateRowLane(nextRowLane, targetRow);
            var sideLane = sourceRow == targetRow ? -1 : nextSideLane++;

            plans.Add(new LaneRoutePlan(
                sourceKey,
                targetKey,
                sourceInternal ? sourceNs!.Name : edge.Source.FullyQualifiedName,
                targetInternal ? targetNs!.Name : edge.Target.FullyQualifiedName,
                edge.Kind,
                sourceRow,
                targetRow,
                sourceLane,
                targetLane,
                sideLane));
        }

        return plans;
    }

    private static int AllocateRowLane(Dictionary<int, int> nextRowLane, int row)
    {
        var lane = nextRowLane.GetValueOrDefault(row);
        nextRowLane[row] = lane + 1;
        return lane;
    }

    private static int[] CountRowLanes(int rowCount, List<LaneRoutePlan> plans)
    {
        var counts = new int[rowCount];
        foreach (var plan in plans)
        {
            counts[plan.SourceRow] = Math.Max(counts[plan.SourceRow], plan.SourceHorizontalLane + 1);
            counts[plan.TargetRow] = Math.Max(counts[plan.TargetRow], plan.TargetHorizontalLane + 1);
        }
        return counts;
    }

    private static double CalculateCentralWidth(
        ProjectGraph graph,
        HashSet<NamespaceNode> visibleFolderNamespaces,
        Dictionary<ClassNode, Size> sizes)
    {
        var width = CentralMinWidth;

        foreach (var ns in graph.Namespaces.Where(ns => !ns.IsInternal || visibleFolderNamespaces.Contains(ns)))
            width = Math.Max(width, CalculateNamespaceContentWidth(ns, sizes));

        width = Math.Max(width, CalculateClassRowWidth(graph.GlobalClasses, sizes));

        foreach (var ns in graph.Namespaces.Where(ns => !ns.IsInternal || visibleFolderNamespaces.Contains(ns)))
        {
            var labelWidth = ns.Name.Length * CharWidth + NsPadding * 2;
            width = Math.Max(width, ns.IsInternal ? Math.Max(labelWidth, FolderMinWidth) : labelWidth);
        }

        return width;
    }

    private static double CalculateNamespaceContentWidth(NamespaceNode ns, Dictionary<ClassNode, Size> sizes)
    {
        if (ns.IsInternal)
            return Math.Max(ns.Name.Length * CharWidth + NsPadding * 2, FolderMinWidth);

        return Math.Max(ns.Name.Length * CharWidth + NsPadding * 2, CalculateClassRowWidth(ns.Classes, sizes));
    }

    private static double CalculateClassRowWidth(IReadOnlyList<ClassNode> classes, Dictionary<ClassNode, Size> sizes)
    {
        if (classes.Count == 0)
            return CentralMinWidth;

        var width = NsPadding * 2;
        var rowWidth = 0.0;
        foreach (var cls in classes)
            rowWidth += sizes[cls].Width + (rowWidth > 0 ? ClassGap : 0);

        return Math.Clamp(width + rowWidth, CentralMinWidth, CentralMaxWidth);
    }

    private static List<Rect> PlaceRows(
        ProjectGraph graph,
        List<LayoutRow> rows,
        int[] rowLaneCounts,
        Dictionary<ClassNode, Size> sizes,
        LayoutResult result,
        double centralX,
        double centralWidth)
    {
        var rowRects = new List<Rect>();
        var y = NsGap;
        var namespaceStartY = new Dictionary<NamespaceNode, double>();
        var namespaceEndY = new Dictionary<NamespaceNode, double>();

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.Namespace != null && !namespaceStartY.ContainsKey(row.Namespace))
                namespaceStartY[row.Namespace] = y;

            var laneHeight = CalculateLaneHeight(rowLaneCounts[rowIndex]);
            var rectY = y + laneHeight;
            Rect rect;

            if (row.IsFolder && row.Namespace != null)
            {
                rect = new Rect(centralX, rectY, Math.Max(FolderMinWidth, centralWidth), FolderHeight);
                result.NamespaceRects[row.Namespace] = rect;
            }
            else if (row.Classes.Count > 0)
            {
                var classX = centralX + NsPadding;
                var maxHeight = row.Classes.Max(cls => sizes[cls].Height);
                foreach (var cls in row.Classes)
                {
                    var size = sizes[cls];
                    var classY = rectY + (maxHeight - size.Height) / 2;
                    result.ClassRects[cls] = new Rect(classX, classY, size.Width, size.Height);
                    classX += size.Width + ClassGap;
                    if (row.Namespace == null)
                        result.GlobalClasses.Add(cls);
                }
                rect = new Rect(centralX, rectY, centralWidth, maxHeight);
            }
            else
            {
                rect = new Rect(centralX, rectY, centralWidth, row.Height);
            }

            rowRects.Add(rect);

            if (row.Namespace != null)
                namespaceEndY[row.Namespace] = rect.Bottom + NsPadding;

            y = rect.Bottom + ClassGap;
        }

        foreach (var ns in graph.Namespaces.Where(ns => !ns.IsInternal && namespaceStartY.ContainsKey(ns)))
        {
            var nsY = namespaceStartY[ns];
            var nsBottom = namespaceEndY[ns];
            result.NamespaceRects[ns] = new Rect(centralX, nsY, centralWidth, Math.Max(NsLabelHeight + NsPadding * 2, nsBottom - nsY));
            result.NamespaceOrder.Add(ns);
        }

        foreach (var ns in graph.Namespaces.Where(ns => ns.IsInternal && result.NamespaceRects.ContainsKey(ns)))
            result.NamespaceOrder.Add(ns);

        return rowRects;
    }

    private static Dictionary<string, Rect> BuildRectByKey(LayoutResult result)
    {
        var map = new Dictionary<string, Rect>(StringComparer.Ordinal);
        foreach (var kv in result.ClassRects)
            map[ClassKey(kv.Key)] = kv.Value;
        foreach (var kv in result.NamespaceRects)
            map[NamespaceKey(kv.Key)] = kv.Value;
        return map;
    }

    private static void RouteArrows(
        List<LaneRoutePlan> plans,
        List<Rect> rowRects,
        Dictionary<string, Rect> rectByKey,
        double centralX,
        double centralWidth,
        LayoutResult result)
    {
        var endpointGroups = BuildEndpointGroups(plans);
        foreach (var plan in plans)
        {
            if (!rectByKey.TryGetValue(plan.SourceKey, out var sourceRect) ||
                !rectByKey.TryGetValue(plan.TargetKey, out var targetRect))
                continue;

            var sourcePoint = TopAttachPoint(sourceRect, EndpointFraction(endpointGroups, plan.SourceKey, plan));
            var targetPoint = TopAttachPoint(targetRect, EndpointFraction(endpointGroups, plan.TargetKey, plan));
            var points = BuildFixedLanePoints(
                plan,
                sourcePoint,
                targetPoint,
                rowRects,
                centralX,
                centralWidth);
            var segments = NormalizeSameDirectionSegments(PointsToSegments(points));
            if (segments.Count == 0)
                continue;

            result.Arrows.Add(new ArrowRoute(
                segments,
                plan.Kind,
                plan.SourceKey,
                plan.TargetKey,
                plan.SourceLabel,
                plan.TargetLabel,
                "Top",
                "Top",
                sourceRect,
                targetRect)
            {
                SourceRow = plan.SourceRow,
                TargetRow = plan.TargetRow,
                SourceHorizontalLane = plan.SourceHorizontalLane,
                TargetHorizontalLane = plan.TargetHorizontalLane,
                SideLane = plan.SideLane
            });
        }
    }

    private static Dictionary<string, List<LaneRoutePlan>> BuildEndpointGroups(List<LaneRoutePlan> plans)
    {
        var groups = new Dictionary<string, List<LaneRoutePlan>>(StringComparer.Ordinal);
        foreach (var plan in plans)
        {
            Add(plan.SourceKey, plan);
            Add(plan.TargetKey, plan);
        }
        return groups;

        void Add(string key, LaneRoutePlan plan)
        {
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<LaneRoutePlan>();
                groups[key] = list;
            }
            list.Add(plan);
        }
    }

    private static double EndpointFraction(
        Dictionary<string, List<LaneRoutePlan>> endpointGroups,
        string key,
        LaneRoutePlan plan)
    {
        var group = endpointGroups[key];
        var index = group.IndexOf(plan);
        return (index + 1.0) / (group.Count + 1.0);
    }

    private static List<Point> BuildFixedLanePoints(
        LaneRoutePlan plan,
        Point sourcePoint,
        Point targetPoint,
        List<Rect> rowRects,
        double centralX,
        double centralWidth)
    {
        var sourceLaneY = HorizontalLaneY(rowRects[plan.SourceRow], plan.SourceHorizontalLane);
        var targetLaneY = HorizontalLaneY(rowRects[plan.TargetRow], plan.TargetHorizontalLane);
        var points = new List<Point> { sourcePoint };

        if (plan.SideLane < 0)
        {
            var laneY = Math.Min(sourceLaneY, targetLaneY);
            points.Add(new Point(sourcePoint.X, laneY));
            points.Add(new Point(targetPoint.X, laneY));
        }
        else
        {
            var sideX = SideLaneX(plan.SideLane, centralX, centralWidth);
            points.Add(new Point(sourcePoint.X, sourceLaneY));
            points.Add(new Point(sideX, sourceLaneY));
            points.Add(new Point(sideX, targetLaneY));
            points.Add(new Point(targetPoint.X, targetLaneY));
        }

        points.Add(targetPoint);
        return RemoveDuplicatePoints(points);
    }

    private static double CalculateLaneHeight(int laneCount) =>
        laneCount == 0
            ? TopConnectionLength
            : laneCount * LaneGap + TopConnectionLength + BottomLaneClearance;

    private static double HorizontalLaneY(Rect rowRect, int lane) =>
        rowRect.Top - TopConnectionLength - BottomLaneClearance - lane * LaneGap;

    private static double SideLaneX(int sideLane, double centralX, double centralWidth)
    {
        var step = sideLane / 2 + 1;
        return sideLane % 2 == 0
            ? centralX - step * SideLaneGap
            : centralX + centralWidth + step * SideLaneGap;
    }

    private static Point TopAttachPoint(Rect rect, double fraction) =>
        new(rect.Left + rect.Width * fraction, rect.Top);

    private static List<RouteSegment> PointsToSegments(List<Point> points)
    {
        var segs = new List<RouteSegment>();
        for (var i = 0; i < points.Count - 1; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                if (Math.Abs(dx) > 0.1)
                    segs.Add(new RouteSegment(from.X, from.Y, dx > 0 ? Direction.Right : Direction.Left, Math.Abs(dx)));
            }
            else if (Math.Abs(dy) > 0.1)
            {
                segs.Add(new RouteSegment(from.X, from.Y, dy > 0 ? Direction.Down : Direction.Up, Math.Abs(dy)));
            }
        }
        return segs;
    }

    private static List<RouteSegment> NormalizeSameDirectionSegments(List<RouteSegment> segs)
    {
        var result = segs.Where(s => s.Length > 0.1).ToList();
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < result.Count - 1; i++)
            {
                var a = result[i];
                var b = result[i + 1];
                if (a.Direction != b.Direction || !PointsClose(a.End, b.Start))
                    continue;

                result[i] = new RouteSegment(a.X, a.Y, a.Direction, a.Length + b.Length);
                result.RemoveAt(i + 1);
                changed = true;
                break;
            }
        }
        return result;
    }

    private static void CalcSize(ClassNode node, Dictionary<ClassNode, Size> sizes)
    {
        var textW = node.Name.Length * CharWidth;
        var lines = textW <= MaxTextWidth ? 1 : Math.Ceiling(textW / MaxTextWidth);
        var w = Math.Clamp(Math.Min(textW, MaxTextWidth) + ClassPadH * 2, MinClassWidth, MaxClassWidth);
        var h = lines * LineHeight + ClassPadV * 2;
        sizes[node] = new Size(w, h);
    }

    private static string ClassKey(ClassNode cls) => $"class:{cls.FullyQualifiedName}";

    private static string NamespaceKey(NamespaceNode ns) => $"namespace:{ns.Name}";

    private static IEnumerable<ClassNode> EnumerateAllTopLevel(ProjectGraph graph)
    {
        foreach (var ns in graph.Namespaces)
            foreach (var cls in ns.Classes)
                yield return cls;
        foreach (var cls in graph.GlobalClasses)
            yield return cls;
    }

    private static List<Point> RemoveDuplicatePoints(List<Point> points)
    {
        var result = new List<Point>();
        foreach (var point in points)
        {
            if (result.Count == 0 || !PointsClose(result[^1], point))
                result.Add(point);
        }
        return result;
    }

    private static bool PointsClose(Point a, Point b) =>
        Math.Abs(a.X - b.X) < 0.1 && Math.Abs(a.Y - b.Y) < 0.1;
}
