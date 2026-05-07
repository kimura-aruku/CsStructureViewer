namespace CsStructureViewer.Models;

public class ProjectGraph
{
    public List<NamespaceNode> Namespaces { get; } = new();
    public List<ClassNode> GlobalClasses { get; } = new();
    public List<DependencyEdge> Edges { get; } = new();
}
