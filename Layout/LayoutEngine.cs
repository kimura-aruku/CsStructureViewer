using System.Windows;
using CsStructureViewer.Models;

namespace CsStructureViewer.Layout;

internal enum Side { Left, Right, Top, Bottom }

internal record RouteRequest(
    string SourceKey,
    string TargetKey,
    Rect SourceRect,
    Rect TargetRect,
    DependencyKind Kind,
    NamespaceNode? SourceNamespace,
    NamespaceNode? TargetNamespace);

public class LayoutEngine
{
    private static readonly double[] LaneOffsets = [0.0, 8.0, -8.0, 16.0, -16.0, 24.0, -24.0];

    private const double CharWidth = 7.5;
    private const double LineHeight = 18.0;
    private const double ClassPadH = 12.0;
    private const double ClassPadV = 8.0;
    private const double MaxTextWidth = 176.0;
    private const double MaxClassWidth = 200.0;
    private const double MinClassWidth = 60.0;
    private const double ClassGap = 30.0;
    private const double NsPadding = 16.0;
    private const double NsLabelHeight = 22.0;
    private const double NsGap = 20.0;
    private const double MaxNsWidth = 600.0;
    private const double RoutingMargin = 18.0;
    private const double FolderMinWidth = 140.0;
    private const double FolderHeight = 54.0;

    public LayoutResult Calculate(ProjectGraph graph, double canvasMaxWidth)
    {
        var result = new LayoutResult();
        var sizes = new Dictionary<ClassNode, Size>();
        var classNamespaces = BuildClassNamespaceMap(graph);
        var visibleFolderNamespaces = GetVisibleFolderNamespaces(graph, classNamespaces);

        // Identify folder namespaces
        foreach (var ns in visibleFolderNamespaces)
        {
            result.FolderNamespaces.Add(ns);
        }

        // Step 1: Calculate sizes for all classes
        foreach (var node in EnumerateAllTopLevel(graph))
            CalcSize(node, sizes);

        // Step 2 & 3: Place classes within namespaces, compute namespace rects
        double x = 0, y = 0, rowH = 0;
        foreach (var ns in graph.Namespaces.Where(ns => !ns.IsInternal || visibleFolderNamespaces.Contains(ns)))
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

        // Step 6: Arrow routes
        var routeRequests = BuildRouteRequests(graph.Edges, classNamespaces, result);

        RouteArrows(routeRequests, result);

        return result;
    }

    // ── Pattern matching ────────────────────────────────────────────

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

    private static List<RouteRequest> BuildRouteRequests(
        List<DependencyEdge> edges,
        Dictionary<ClassNode, NamespaceNode> classNamespaces,
        LayoutResult result)
    {
        var requests = new List<RouteRequest>();
        var seen = new HashSet<(string sourceKey, string targetKey, DependencyKind kind)>();

        foreach (var edge in edges)
        {
            var sourceNs = classNamespaces.GetValueOrDefault(edge.Source);
            var targetNs = classNamespaces.GetValueOrDefault(edge.Target);
            var sourceInternal = sourceNs?.IsInternal == true;
            var targetInternal = targetNs?.IsInternal == true;

            if (sourceInternal && targetInternal)
                continue;

            var sourceKey = sourceInternal ? NamespaceKey(sourceNs!) : ClassKey(edge.Source);
            var targetKey = targetInternal ? NamespaceKey(targetNs!) : ClassKey(edge.Target);
            if (!seen.Add((sourceKey, targetKey, edge.Kind)))
                continue;

            if (!TryGetEndpointRect(edge.Source, sourceInternal ? sourceNs : null, result, out var sourceRect) ||
                !TryGetEndpointRect(edge.Target, targetInternal ? targetNs : null, result, out var targetRect))
                continue;

            requests.Add(new RouteRequest(
                sourceKey,
                targetKey,
                sourceRect,
                targetRect,
                edge.Kind,
                sourceNs,
                targetNs));
        }

        return requests;
    }

    private static bool TryGetEndpointRect(
        ClassNode cls,
        NamespaceNode? internalNamespace,
        LayoutResult result,
        out Rect rect)
    {
        if (internalNamespace != null)
            return result.NamespaceRects.TryGetValue(internalNamespace, out rect);

        return result.ClassRects.TryGetValue(cls, out rect);
    }

    private static string ClassKey(ClassNode cls) => $"class:{cls.FullyQualifiedName}";

    private static string NamespaceKey(NamespaceNode ns) => $"namespace:{ns.Name}";

    // ── Arrow routing ────────────────────────────────────────────────

    private static void RouteArrows(
        List<RouteRequest> requests,
        LayoutResult result)
    {
        if (requests.Count == 0) return;

        var sideInfo = SelectSidePairs(requests, result);

        // Group by (node, side) for spread calculation
        var groups = new Dictionary<(string key, Side side), List<(int idx, bool isSrc)>>();
        for (int i = 0; i < requests.Count; i++)
        {
            var (srcSide, tgtSide) = sideInfo[i];
            AddToGroup(groups, (requests[i].SourceKey, srcSide), (i, true));
            AddToGroup(groups, (requests[i].TargetKey, tgtSide), (i, false));
        }

        var srcFractions = new double[requests.Count];
        var tgtFractions = new double[requests.Count];
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

        for (int i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            var srcRect = request.SourceRect;
            var tgtRect = request.TargetRect;
            var (srcSide, tgtSide) = sideInfo[i];

            var srcPt = AttachPoint(srcRect, srcSide, srcFractions[i]);
            var tgtPt = AttachPoint(tgtRect, tgtSide, tgtFractions[i]);

            var obstacles = BuildObstacles(request, result);
            List<RouteSegment>? segs = null;
            foreach (var laneOffset in LaneOffsets)
            {
                var candidate = BuildRouteSegments(
                    srcPt, srcSide, tgtPt, tgtSide, srcRect, tgtRect, obstacles, laneOffset);
                if (candidate.Count == 0) continue;

                segs = candidate;
                if (!OverlapsExistingRoute(candidate, result.Arrows))
                    break;
            }

            if (segs is { Count: > 0 })
                result.Arrows.Add(new ArrowRoute(segs, request.Kind));
        }
    }

    private static List<RouteSegment> BuildRouteSegments(
        Point srcPt,
        Side srcSide,
        Point tgtPt,
        Side tgtSide,
        Rect srcRect,
        Rect tgtRect,
        List<Rect> obstacles,
        double laneOffset)
    {
        var pts = RouteOrthogonal(srcPt, srcSide, tgtPt, tgtSide, srcRect, tgtRect, laneOffset);
        var allSegs = PointsToSegments(pts);
        if (allSegs.Count == 0) return [];

        // 最初(srcPt→exitPt)と最後(entryPt→tgtPt)は辺への接続セグメント。
        // AvoidObstacles に渡すと方向が変わり辺との整合が壊れるため対象外にする。
        if (allSegs.Count == 1)
            return NormalizeSegments(allSegs);

        var firstSeg = allSegs[0];
        var lastSeg  = allSegs[^1];
        var middleSegs = allSegs.Count > 2
            ? allSegs.GetRange(1, allSegs.Count - 2)
            : new List<RouteSegment>();

        middleSegs = AvoidObstacles(middleSegs, obstacles);
        middleSegs = NormalizeSegments(middleSegs);

        var segs = new List<RouteSegment> { firstSeg };
        segs.AddRange(middleSegs);
        segs.Add(lastSeg);
        return SimplifyMiddleRoute(NormalizeSegments(segs), obstacles);
    }

    private static (Side srcSide, Side tgtSide)[] SelectSidePairs(
        List<RouteRequest> requests,
        LayoutResult result)
    {
        var selected = new (Side srcSide, Side tgtSide)[requests.Count];

        for (int i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            var srcRect = request.SourceRect;
            var tgtRect = request.TargetRect;
            var natural = DetermineNaturalSides(srcRect, tgtRect);
            var obstacles = BuildObstacles(request, result);

            var bestScore = double.PositiveInfinity;
            var bestPair = natural;

            foreach (var pair in EnumerateSidePairCandidates(natural))
            {
                var srcPt = AttachPoint(srcRect, pair.srcSide, 0.5);
                var tgtPt = AttachPoint(tgtRect, pair.tgtSide, 0.5);
                var route = BuildRouteSegments(
                    srcPt, pair.srcSide, tgtPt, pair.tgtSide, srcRect, tgtRect, obstacles, laneOffset: 0);
                var score = ScoreRoute(route, obstacles, pair, natural, srcRect, tgtRect);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestPair = pair;
                }
            }

            selected[i] = bestPair;
        }

        return selected;
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

    private static IEnumerable<(Side srcSide, Side tgtSide)> EnumerateSidePairCandidates(
        (Side srcSide, Side tgtSide) natural)
    {
        yield return natural;

        var candidates = new (Side srcSide, Side tgtSide)[]
        {
            (Side.Right, Side.Left),
            (Side.Left, Side.Right),
            (Side.Bottom, Side.Top),
            (Side.Top, Side.Bottom),
            (Side.Right, Side.Right),
            (Side.Left, Side.Left),
            (Side.Bottom, Side.Bottom),
            (Side.Top, Side.Top),
        };

        foreach (var candidate in candidates)
            if (candidate != natural)
                yield return candidate;
    }

    private static Point AttachPoint(Rect rect, Side side, double fraction) => side switch
    {
        Side.Left   => new Point(rect.Left,  rect.Top + rect.Height * fraction),
        Side.Right  => new Point(rect.Right, rect.Top + rect.Height * fraction),
        Side.Top    => new Point(rect.Left + rect.Width * fraction, rect.Top),
        Side.Bottom => new Point(rect.Left + rect.Width * fraction, rect.Bottom),
        _ => Center(rect)
    };

    private static List<Point> RouteOrthogonal(
        Point srcPt,
        Side srcSide,
        Point tgtPt,
        Side tgtSide,
        Rect srcRect,
        Rect tgtRect,
        double laneOffset)
    {
        var pts = new List<Point> { srcPt };
        var exitPt  = Extend(srcPt, srcSide, RoutingMargin);
        var entryPt = Extend(tgtPt, tgtSide, RoutingMargin);

        bool srcH = srcSide is Side.Left or Side.Right;
        bool tgtH = tgtSide is Side.Left or Side.Right;

        if (srcH && tgtH)
        {
            if (srcSide == tgtSide)
            {
                double extreme = srcSide == Side.Right
                    ? Math.Max(srcRect.Right, tgtRect.Right) + 30 + Math.Abs(laneOffset)
                    : Math.Min(srcRect.Left, tgtRect.Left) - 30 - Math.Abs(laneOffset);
                pts.Add(exitPt);
                pts.Add(new Point(extreme, exitPt.Y));
                pts.Add(new Point(extreme, tgtPt.Y));
                pts.Add(tgtPt);
            }
            else
            {
                // tgt が src より手前（srcSide と逆方向）にある場合は逆方向セグメントが
                // 発生するためループ型に切り替える
                bool wouldReverse = (srcSide == Side.Right && tgtPt.X < srcPt.X) ||
                                    (srcSide == Side.Left  && tgtPt.X > srcPt.X);
                if (wouldReverse)
                {
                    double extreme = srcSide == Side.Right
                        ? Math.Max(exitPt.X, entryPt.X) + 30 + Math.Abs(laneOffset)
                        : Math.Min(exitPt.X, entryPt.X) - 30 - Math.Abs(laneOffset);
                    pts.Add(exitPt);
                    pts.Add(new Point(extreme, exitPt.Y));
                    pts.Add(new Point(extreme, entryPt.Y));
                    pts.Add(entryPt);
                }
                else
                {
                    double midX = (srcPt.X + tgtPt.X) / 2 + laneOffset;
                    if (Math.Abs(exitPt.Y - entryPt.Y) < 1)
                    {
                        pts.Add(tgtPt);
                    }
                    else
                    {
                        pts.Add(new Point(midX, srcPt.Y));
                        pts.Add(new Point(midX, tgtPt.Y));
                        pts.Add(tgtPt);
                    }
                }
            }
        }
        else if (!srcH && !tgtH)
        {
            if (srcSide == tgtSide)
            {
                double extreme = srcSide == Side.Bottom
                    ? Math.Max(srcRect.Bottom, tgtRect.Bottom) + 30 + Math.Abs(laneOffset)
                    : Math.Min(srcRect.Top, tgtRect.Top) - 30 - Math.Abs(laneOffset);
                pts.Add(exitPt);
                pts.Add(new Point(exitPt.X, extreme));
                pts.Add(new Point(tgtPt.X, extreme));
                pts.Add(tgtPt);
            }
            else
            {
                // tgt が src より手前（srcSide と逆方向）にある場合はループ型に切り替える
                bool wouldReverse = (srcSide == Side.Bottom && tgtPt.Y < srcPt.Y) ||
                                    (srcSide == Side.Top    && tgtPt.Y > srcPt.Y);
                if (wouldReverse)
                {
                    double extreme = srcSide == Side.Bottom
                        ? Math.Max(exitPt.Y, entryPt.Y) + 30 + Math.Abs(laneOffset)
                        : Math.Min(exitPt.Y, entryPt.Y) - 30 - Math.Abs(laneOffset);
                    pts.Add(exitPt);
                    pts.Add(new Point(exitPt.X, extreme));
                    pts.Add(new Point(entryPt.X, extreme));
                    pts.Add(entryPt);
                }
                else
                {
                    double midY = (srcPt.Y + tgtPt.Y) / 2 + laneOffset;
                    if (Math.Abs(exitPt.X - entryPt.X) < 1)
                    {
                        pts.Add(tgtPt);
                    }
                    else
                    {
                        pts.Add(new Point(srcPt.X, midY));
                        pts.Add(new Point(tgtPt.X, midY));
                        pts.Add(tgtPt);
                    }
                }
            }
        }
        else if (srcH)
        {
            // src が水平接続（Left/Right）、tgt が垂直接続（Top/Bottom）
            // exitPt から先に垂直移動してから水平移動で entryPt へ向かう。
            // 逆（先に水平）だと srcSide と逆方向のセグメントが生まれる場合がある。
            pts.Add(exitPt);
            if (Math.Abs(laneOffset) < 0.1)
            {
                pts.Add(new Point(exitPt.X, entryPt.Y));
            }
            else
            {
                var laneX = exitPt.X + (srcSide == Side.Right ? Math.Abs(laneOffset) : -Math.Abs(laneOffset));
                pts.Add(new Point(laneX, exitPt.Y));
                pts.Add(new Point(laneX, entryPt.Y));
            }
            pts.Add(entryPt);
        }
        else
        {
            // src が垂直接続（Top/Bottom）、tgt が水平接続（Left/Right）
            // exitPt から先に水平移動してから垂直移動で entryPt へ向かう。
            pts.Add(exitPt);
            if (Math.Abs(laneOffset) < 0.1)
            {
                pts.Add(new Point(entryPt.X, exitPt.Y));
            }
            else
            {
                var laneY = exitPt.Y + (srcSide == Side.Bottom ? Math.Abs(laneOffset) : -Math.Abs(laneOffset));
                pts.Add(new Point(exitPt.X, laneY));
                pts.Add(new Point(entryPt.X, laneY));
            }
            pts.Add(entryPt);
        }

        if (!PointsClose(pts[^1], tgtPt))
            pts.Add(tgtPt);
        return pts;
    }

    private static Point Extend(Point pt, Side side, double margin) => side switch
    {
        Side.Right  => new Point(pt.X + margin, pt.Y),
        Side.Left   => new Point(pt.X - margin, pt.Y),
        Side.Bottom => new Point(pt.X, pt.Y + margin),
        Side.Top    => new Point(pt.X, pt.Y - margin),
        _ => pt
    };

    // ── Point list → RouteSegment list ──────────────────────────────

    private static List<RouteSegment> PointsToSegments(List<Point> points)
    {
        var segs = new List<RouteSegment>();
        for (int i = 0; i < points.Count - 1; i++)
        {
            var from = points[i];
            var to   = points[i + 1];
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                if (Math.Abs(dx) > 0.1)
                    segs.Add(new RouteSegment(from.X, from.Y,
                        dx > 0 ? Direction.Right : Direction.Left, Math.Abs(dx)));
            }
            else
            {
                if (Math.Abs(dy) > 0.1)
                    segs.Add(new RouteSegment(from.X, from.Y,
                        dy > 0 ? Direction.Down : Direction.Up, Math.Abs(dy)));
            }
        }
        return segs;
    }

    // ── Normalize: merge same-direction segments only ──────────────
    // 逆方向キャンセルは行わない。キャンセルすると先頭・末尾セグメントの方向が
    // srcSide/tgtSide と一致しなくなり、接続辺と矢印方向がずれる原因になるため。

    private static List<RouteSegment> NormalizeSegments(List<RouteSegment> segs)
    {
        var result = segs.Where(s => s.Length > 0.1).ToList();

        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < result.Count - 1; i++)
            {
                var a = result[i];
                var b = result[i + 1];

                if (a.Direction == b.Direction && PointsClose(a.End, b.Start))
                {
                    result[i] = new RouteSegment(a.X, a.Y, a.Direction, a.Length + b.Length);
                    result.RemoveAt(i + 1);
                    changed = true;
                    break;
                }

                if (ArrowRoute.AreOpposite(a.Direction, b.Direction) && PointsClose(a.End, b.Start))
                {
                    var replacement = MergeOppositeSegments(a, b);
                    result.RemoveAt(i + 1);
                    result.RemoveAt(i);
                    if (replacement != null)
                        result.Insert(i, replacement);
                    changed = true;
                    break;
                }
            }
        } while (changed);

        return result.Where(s => s.Length > 0.1).ToList();
    }

    private static RouteSegment? MergeOppositeSegments(RouteSegment a, RouteSegment b)
    {
        var diff = a.Length - b.Length;
        if (Math.Abs(diff) < 0.1)
            return null;

        if (diff > 0)
            return new RouteSegment(a.X, a.Y, a.Direction, diff);

        return new RouteSegment(a.X, a.Y, b.Direction, -diff);
    }

    // ── Obstacle avoidance ──────────────────────────────────────────

    private static List<Rect> BuildObstacles(
        RouteRequest request,
        LayoutResult result)
    {
        var obstacles = new List<Rect>();
        foreach (var kv in result.ClassRects)
        {
            if (RectClose(kv.Value, request.SourceRect) || RectClose(kv.Value, request.TargetRect))
                continue;
            obstacles.Add(kv.Value);
        }
        foreach (var kv in result.NamespaceRects)
        {
            if (kv.Key == request.SourceNamespace || kv.Key == request.TargetNamespace)
                continue;
            obstacles.Add(kv.Value);
        }
        return obstacles;
    }

    private static List<RouteSegment> AvoidObstacles(List<RouteSegment> segs, List<Rect> obstacles)
    {
        const int maxPasses = 5;
        for (int pass = 0; pass < maxPasses; pass++)
        {
            bool changed = false;
            var next = new List<RouteSegment>();
            foreach (var seg in segs)
            {
                var bypass = TryBypassSegment(seg, obstacles);
                if (bypass != null)
                {
                    next.AddRange(bypass);
                    changed = true;
                }
                else
                {
                    next.Add(seg);
                }
            }
            segs = NormalizeSegments(next);
            if (!changed) break;
        }
        return segs;
    }

    private static List<RouteSegment>? TryBypassSegment(RouteSegment seg, List<Rect> obstacles)
    {
        foreach (var rect in obstacles)
        {
            if (!SegmentIntersectsRect(seg, rect)) continue;
            return GenerateBypass(seg, rect);
        }
        return null;
    }

    private static List<RouteSegment> GenerateBypass(RouteSegment seg, Rect rect)
    {
        bool isHoriz = seg.Direction is Direction.Right or Direction.Left;
        if (isHoriz)
        {
            double y   = seg.Y;
            double x1  = seg.X;
            double x2  = seg.End.X;
            double detourY = y < rect.Top + rect.Height / 2
                ? rect.Top    - RoutingMargin
                : rect.Bottom + RoutingMargin;
            var vertOut  = detourY < y ? Direction.Up   : Direction.Down;
            var vertBack = detourY < y ? Direction.Down : Direction.Up;
            double vLen = Math.Abs(y - detourY);
            double hLen = Math.Abs(x2 - x1);
            return
            [
                new RouteSegment(x1, y,       vertOut,       vLen),
                new RouteSegment(x1, detourY, seg.Direction, hLen),
                new RouteSegment(x2, detourY, vertBack,      vLen),
            ];
        }
        else
        {
            double x   = seg.X;
            double y1  = seg.Y;
            double y2  = seg.End.Y;
            double detourX = x < rect.Left + rect.Width / 2
                ? rect.Left  - RoutingMargin
                : rect.Right + RoutingMargin;
            var horizOut  = detourX < x ? Direction.Left  : Direction.Right;
            var horizBack = detourX < x ? Direction.Right : Direction.Left;
            double hLen = Math.Abs(x - detourX);
            double vLen = Math.Abs(y2 - y1);
            return
            [
                new RouteSegment(x,       y1, horizOut,      hLen),
                new RouteSegment(detourX, y1, seg.Direction, vLen),
                new RouteSegment(detourX, y2, horizBack,     hLen),
            ];
        }
    }

    private static bool SegmentIntersectsRect(RouteSegment seg, Rect rect)
    {
        bool isHoriz = seg.Direction is Direction.Right or Direction.Left;
        if (isHoriz)
        {
            double y = seg.Y;
            if (y <= rect.Top || y >= rect.Bottom) return false;
            double minX = Math.Min(seg.X, seg.End.X);
            double maxX = Math.Max(seg.X, seg.End.X);
            return maxX > rect.Left && minX < rect.Right;
        }
        else
        {
            double x = seg.X;
            if (x <= rect.Left || x >= rect.Right) return false;
            double minY = Math.Min(seg.Y, seg.End.Y);
            double maxY = Math.Max(seg.Y, seg.End.Y);
            return maxY > rect.Top && minY < rect.Bottom;
        }
    }

    private static double ScoreRoute(
        List<RouteSegment> route,
        List<Rect> obstacles,
        (Side srcSide, Side tgtSide) pair,
        (Side srcSide, Side tgtSide) natural,
        Rect srcRect,
        Rect tgtRect)
    {
        if (route.Count == 0) return double.PositiveInfinity;

        var score = route.Sum(s => s.Length);
        score += Math.Max(0, route.Count - 1) * 180.0;
        score += SidePairPenalty(pair, natural);
        score += DirectionConsistencyPenalty(pair, srcRect, tgtRect);

        if (pair != natural)
            score += 40.0;

        if (RouteIntersectsObstacles(route, obstacles))
            score += 100000.0;

        return score;
    }

    private static bool RouteIntersectsObstacles(List<RouteSegment> route, List<Rect> obstacles) =>
        route.Any(seg => obstacles.Any(rect => SegmentIntersectsRect(seg, rect)));

    private static double SidePairPenalty(
        (Side srcSide, Side tgtSide) pair,
        (Side srcSide, Side tgtSide) natural)
    {
        var penalty = SideDistance(pair.srcSide, natural.srcSide) * 220.0;
        penalty += SideDistance(pair.tgtSide, natural.tgtSide) * 220.0;

        if (pair.srcSide == pair.tgtSide)
            penalty += 260.0;

        return penalty;
    }

    private static double DirectionConsistencyPenalty(
        (Side srcSide, Side tgtSide) pair,
        Rect srcRect,
        Rect tgtRect)
    {
        var dx = Center(tgtRect).X - Center(srcRect).X;
        var dy = Center(tgtRect).Y - Center(srcRect).Y;
        var horizontalDominant = Math.Abs(dx) >= Math.Abs(dy);

        return pair switch
        {
            (Side.Bottom, Side.Top) when dy <= 0 => 1200.0,
            (Side.Top, Side.Bottom) when dy >= 0 => 1200.0,
            (Side.Right, Side.Left) when dx <= 0 => 1200.0,
            (Side.Left, Side.Right) when dx >= 0 => 1200.0,
            (Side.Bottom, Side.Top) when horizontalDominant => 900.0,
            (Side.Top, Side.Bottom) when horizontalDominant => 900.0,
            (Side.Right, Side.Left) when !horizontalDominant => 900.0,
            (Side.Left, Side.Right) when !horizontalDominant => 900.0,
            _ => 0.0
        };
    }

    private static int SideDistance(Side a, Side b)
    {
        if (a == b) return 0;
        return IsOppositeSide(a, b) ? 2 : 1;
    }

    private static bool IsOppositeSide(Side a, Side b) =>
        (a == Side.Left && b == Side.Right) ||
        (a == Side.Right && b == Side.Left) ||
        (a == Side.Top && b == Side.Bottom) ||
        (a == Side.Bottom && b == Side.Top);

    private static List<RouteSegment> SimplifyMiddleRoute(List<RouteSegment> route, List<Rect> obstacles)
    {
        if (route.Count <= 2) return route;

        var firstSeg = route[0];
        var lastSeg = route[^1];
        var middle = route.GetRange(1, route.Count - 2);
        var preferVerticalFirst = IsVertical(firstSeg.Direction) && IsVertical(lastSeg.Direction);
        middle = SimplifyRoute(middle, obstacles, preferVerticalFirst);

        var result = new List<RouteSegment> { firstSeg };
        result.AddRange(middle);
        result.Add(lastSeg);
        return NormalizeSegments(RemoveRouteCycles(result));
    }

    private static List<RouteSegment> SimplifyRoute(
        List<RouteSegment> route,
        List<Rect> obstacles,
        bool preferVerticalFirst = false)
    {
        var current = RemoveRouteCycles(route);
        bool changed;

        do
        {
            changed = false;
            current = RemoveRouteCycles(current);
            for (var i = 0; i < current.Count - 2; i++)
            {
                var replacement = TryShortcut(current[i].Start, current[i + 2].End, obstacles, preferVerticalFirst);
                if (replacement == null) continue;

                var next = new List<RouteSegment>();
                next.AddRange(current.Take(i));
                next.AddRange(replacement);
                next.AddRange(current.Skip(i + 3));
                current = NormalizeSegments(next);
                changed = true;
                break;
            }
        } while (changed);

        return current;
    }

    private static List<RouteSegment> RemoveRouteCycles(List<RouteSegment> route)
    {
        var current = route;
        bool changed;

        do
        {
            changed = false;
            var points = SegmentsToPoints(current);

            for (var i = 0; i < points.Count - 1; i++)
            {
                for (var j = points.Count - 1; j > i + 1; j--)
                {
                    if (!PointsClose(points[i], points[j])) continue;

                    var nextPoints = new List<Point>();
                    nextPoints.AddRange(points.Take(i + 1));
                    nextPoints.AddRange(points.Skip(j + 1));
                    current = PointsToSegments(nextPoints);
                    changed = true;
                    break;
                }

                if (changed) break;
            }
        } while (changed);

        return current;
    }

    private static List<Point> SegmentsToPoints(List<RouteSegment> route)
    {
        if (route.Count == 0) return [];

        var points = new List<Point> { route[0].Start };
        points.AddRange(route.Select(seg => seg.End));
        return points;
    }

    private static List<RouteSegment>? TryShortcut(
        Point start,
        Point end,
        List<Rect> obstacles,
        bool preferVerticalFirst)
    {
        var direct = PointsCloseX(start, end) || PointsCloseY(start, end)
            ? PointsToSegments([start, end])
            : [];
        if (direct.Count == 1 && !RouteIntersectsObstacles(direct, obstacles))
            return direct;

        var verticalFirst = PointsToSegments([start, new Point(start.X, end.Y), end]);
        var horizontalFirst = PointsToSegments([start, new Point(end.X, start.Y), end]);
        var primary = preferVerticalFirst ? verticalFirst : horizontalFirst;
        var secondary = preferVerticalFirst ? horizontalFirst : verticalFirst;

        if (primary.Count > 0 && !RouteIntersectsObstacles(primary, obstacles))
            return primary;

        if (secondary.Count > 0 && !RouteIntersectsObstacles(secondary, obstacles))
            return secondary;

        return null;
    }

    private static bool OverlapsExistingRoute(List<RouteSegment> candidate, List<ArrowRoute> existingRoutes)
    {
        foreach (var candidateSeg in candidate)
        {
            foreach (var route in existingRoutes)
            {
                foreach (var existingSeg in route.Segments)
                {
                    if (SegmentsOverlap(candidateSeg, existingSeg))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsOverlap(RouteSegment a, RouteSegment b)
    {
        var aHoriz = a.Direction is Direction.Right or Direction.Left;
        var bHoriz = b.Direction is Direction.Right or Direction.Left;
        if (aHoriz != bHoriz) return false;

        if (aHoriz)
        {
            if (Math.Abs(a.Y - b.Y) >= 0.1) return false;
            return RangesOverlap(a.X, a.End.X, b.X, b.End.X);
        }

        if (Math.Abs(a.X - b.X) >= 0.1) return false;
        return RangesOverlap(a.Y, a.End.Y, b.Y, b.End.Y);
    }

    private static bool RangesOverlap(double a1, double a2, double b1, double b2)
    {
        var minA = Math.Min(a1, a2);
        var maxA = Math.Max(a1, a2);
        var minB = Math.Min(b1, b2);
        var maxB = Math.Max(b1, b2);
        return Math.Min(maxA, maxB) - Math.Max(minA, minB) > 0.1;
    }

    // ── Size calculation ────────────────────────────────────────────

    private static void CalcSize(ClassNode node, Dictionary<ClassNode, Size> sizes)
    {
        var textW = node.Name.Length * CharWidth;
        double lines = textW <= MaxTextWidth ? 1 : Math.Ceiling(textW / MaxTextWidth);
        double w = Math.Clamp(Math.Min(textW, MaxTextWidth) + ClassPadH * 2, MinClassWidth, MaxClassWidth);
        double h = lines * LineHeight + ClassPadV * 2;
        sizes[node] = new Size(w, h);
    }

    // ── Namespace layout ────────────────────────────────────────────

    private static Size CalcNamespaceLayout(
        NamespaceNode ns, Dictionary<ClassNode, Size> sizes,
        Dictionary<ClassNode, Rect> classRects, double originX, double originY)
    {
        if (ns.IsInternal)
            return CalcFolderNamespaceSize(ns);

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

    private static Size CalcFolderNamespaceSize(NamespaceNode ns)
    {
        var labelWidth = ns.Name.Length * CharWidth + NsPadding * 2;
        return new Size(Math.Max(labelWidth, FolderMinWidth), FolderHeight);
    }

    // ── Class placement ─────────────────────────────────────────────

    private static void PlaceClass(
        ClassNode node, double ax, double ay,
        Dictionary<ClassNode, Size> sizes, Dictionary<ClassNode, Rect> classRects)
    {
        var sz = sizes[node];
        classRects[node] = new Rect(ax, ay, sz.Width, sz.Height);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static bool PointsClose(Point a, Point b) =>
        Math.Abs(a.X - b.X) < 0.1 && Math.Abs(a.Y - b.Y) < 0.1;

    private static bool RectClose(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) < 0.1 &&
        Math.Abs(a.Y - b.Y) < 0.1 &&
        Math.Abs(a.Width - b.Width) < 0.1 &&
        Math.Abs(a.Height - b.Height) < 0.1;

    private static bool PointsCloseX(Point a, Point b) => Math.Abs(a.X - b.X) < 0.1;

    private static bool PointsCloseY(Point a, Point b) => Math.Abs(a.Y - b.Y) < 0.1;

    private static bool IsVertical(Direction direction) => direction is Direction.Up or Direction.Down;

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
