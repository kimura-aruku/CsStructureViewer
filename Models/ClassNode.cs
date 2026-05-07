namespace CsStructureViewer.Models;

public class ClassNode
{
    public string Name { get; set; } = string.Empty;
    public string FullyQualifiedName { get; set; } = string.Empty;
    public string? NamespaceName { get; set; }
    public bool IsPartial { get; set; }
    public TypeKind Kind { get; set; } = TypeKind.Class;
    public List<ClassNode> NestedClasses { get; } = new();
}

public enum TypeKind
{
    Class,
    Interface,
    Struct,
    Enum,
    Record
}
