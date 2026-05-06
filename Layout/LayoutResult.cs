using System.Windows;
using CsStructureViewer.Models;

namespace CsStructureViewer.Layout;

public enum Direction { Up, Right, Down, Left }

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
    public DependencyKind Kind { get; }

    public ArrowRoute(IReadOnlyList<RouteSegment> segments, DependencyKind kind)
    {
        ValidateSegments(segments);
        Segments = segments;
        Kind = kind;
    }

    private static void ValidateSegments(IReadOnlyList<RouteSegment> segs)
    {
        for (int i = 1; i < segs.Count; i++)
        {
            var prev = segs[i - 1].Direction;
            var curr = segs[i].Direction;
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

    public Point Start => Segments.Count > 0 ? Segments[0].Start : new();
    public Point End   => Segments.Count > 0 ? Segments[^1].End  : new();
}

public class LayoutResult
{
    public List<NamespaceNode> NamespaceOrder { get; } = new();
    public List<ClassNode> GlobalClasses { get; } = new();
    public Dictionary<ClassNode, Rect> ClassRects { get; } = new();
    public Dictionary<NamespaceNode, Rect> NamespaceRects { get; } = new();
    public HashSet<NamespaceNode> FolderNamespaces { get; } = new();
    public List<ArrowRoute> Arrows { get; } = new();
}
