namespace CsStructureViewer.Models;

public class DependencyEdge
{
    public ClassNode Source { get; set; } = null!;
    public ClassNode Target { get; set; } = null!;
    public DependencyKind Kind { get; set; }
}

public enum DependencyKind
{
    Inheritance,
    Implementation,
    FieldReference
}
