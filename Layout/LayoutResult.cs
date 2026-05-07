using System.Windows;
using CsStructureViewer.Models;

namespace CsStructureViewer.Layout;

public enum Direction { Up, Right, Down, Left }

public enum GraphDisplayMode { Class, Namespace }

/// <summary>
/// 始点座標・方向・長さで一本の直線を完全に定義する。
/// </summary>
public class RouteSegment
{
    public double X { get; }
    public double Y { get; }
    public Direction Direction { get; }
    public double Length { get; }

    public RouteSegment(double x, double y, Direction direction, double length)
    {
        X = x;
        Y = y;
        Direction = direction;
        Length = length;
    }

    public Point Start => new(X, Y);
    public Point End => Direction switch
    {
        Direction.Right => new(X + Length, Y),
        Direction.Left  => new(X - Length, Y),
        Direction.Down  => new(X, Y + Length),
        Direction.Up    => new(X, Y - Length),
        _ => new(X, Y)
    };
}

/// <summary>
/// 複数の RouteSegment で構成される矢印線。
/// 連続するセグメントが同方向・真逆方向になることは許容しない。
/// </summary>
public class ArrowRoute
{
    public IReadOnlyList<RouteSegment> Segments { get; }
    public List<Point> RenderedShaftPoints { get; } = new();
    public List<Point> RenderedHeadPoints { get; } = new();
    public DependencyKind Kind { get; }
    public string SourceKey { get; }
    public string TargetKey { get; }
    public string SourceLabel { get; }
    public string TargetLabel { get; }
    public string SourceSide { get; }
    public string TargetSide { get; }
    public Rect SourceRect { get; }
    public Rect TargetRect { get; }
    public int SourceRow { get; init; } = -1;
    public int TargetRow { get; init; } = -1;
    public int SourceHorizontalLane { get; init; } = -1;
    public int TargetHorizontalLane { get; init; } = -1;
    public int SideLane { get; init; } = -1;

    public ArrowRoute(
        IReadOnlyList<RouteSegment> segments,
        DependencyKind kind,
        string sourceKey = "",
        string targetKey = "",
        string sourceLabel = "",
        string targetLabel = "",
        string sourceSide = "",
        string targetSide = "",
        Rect sourceRect = default,
        Rect targetRect = default)
    {
        ValidateSegments(segments);
        Segments = segments;
        Kind = kind;
        SourceKey = sourceKey;
        TargetKey = targetKey;
        SourceLabel = sourceLabel;
        TargetLabel = targetLabel;
        SourceSide = sourceSide;
        TargetSide = targetSide;
        SourceRect = sourceRect;
        TargetRect = targetRect;
    }

    private static void ValidateSegments(IReadOnlyList<RouteSegment> segs)
    {
        for (int i = 1; i < segs.Count; i++)
        {
            var prevSeg = segs[i - 1];
            var currSeg = segs[i];
            var prev = prevSeg.Direction;
            var curr = currSeg.Direction;
            if (!PointsClose(prevSeg.End, currSeg.Start))
                throw new InvalidOperationException(
                    $"Segment[{i}] start ({currSeg.Start}) is not connected to segment[{i - 1}] end ({prevSeg.End}).");
            if (curr == prev)
                throw new InvalidOperationException(
                    $"Segment[{i}] has the same direction ({curr}) as segment[{i - 1}].");
            // 逆方向チェックは除去。AvoidObstacles が一時的に逆方向セグメントを
            // 生成するケースがあり、それを拒否すると矢印が描画されなくなるため。
        }
    }

    internal static bool AreOpposite(Direction a, Direction b) =>
        (a == Direction.Up    && b == Direction.Down)  ||
        (a == Direction.Down  && b == Direction.Up)    ||
        (a == Direction.Left  && b == Direction.Right) ||
        (a == Direction.Right && b == Direction.Left);

    private static bool PointsClose(Point a, Point b) =>
        Math.Abs(a.X - b.X) < 0.1 && Math.Abs(a.Y - b.Y) < 0.1;

    public Point Start => Segments.Count > 0 ? Segments[0].Start : new();
    public Point End   => Segments.Count > 0 ? Segments[^1].End  : new();
}

public class LayoutResult
{
    public string ProjectPath { get; set; } = string.Empty;
    public GraphDisplayMode DisplayMode { get; set; } = GraphDisplayMode.Class;
    public List<NamespaceNode> NamespaceOrder { get; } = new();
    public List<ClassNode> GlobalClasses { get; } = new();
    public Dictionary<ClassNode, Rect> ClassRects { get; } = new();
    public Dictionary<NamespaceNode, Rect> NamespaceRects { get; } = new();
    public HashSet<NamespaceNode> FolderNamespaces { get; } = new();
    public List<ArrowRoute> Arrows { get; } = new();
}
