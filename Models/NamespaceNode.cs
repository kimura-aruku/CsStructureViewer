namespace CsStructureViewer.Models;

public class NamespaceNode
{
    public string Name { get; set; } = string.Empty;
    public List<ClassNode> Classes { get; } = new();
}
