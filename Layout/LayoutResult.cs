using System.Windows;
using CsStructureViewer.Models;

namespace CsStructureViewer.Layout;

public record ArrowRoute(IReadOnlyList<Point> Waypoints, DependencyKind Kind)
{
    public Point Start => Waypoints[0];
    public Point End => Waypoints[^1];
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
