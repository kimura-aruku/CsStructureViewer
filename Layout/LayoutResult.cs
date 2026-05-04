using System.Windows;
using CsStructureViewer.Models;

namespace CsStructureViewer.Layout;

public record ArrowRoute(Point Start, Point End, DependencyKind Kind);

public class LayoutResult
{
    public List<NamespaceNode> NamespaceOrder { get; } = new();
    public List<ClassNode> GlobalClasses { get; } = new();
    public Dictionary<ClassNode, Rect> ClassRects { get; } = new();
    public Dictionary<NamespaceNode, Rect> NamespaceRects { get; } = new();
    public List<ArrowRoute> Arrows { get; } = new();
}
