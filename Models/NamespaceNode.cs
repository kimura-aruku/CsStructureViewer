namespace CsStructureViewer.Models;

public class NamespaceNode
{
    public string Name { get; set; } = string.Empty;
    public List<ClassNode> Classes { get; } = new();
    public bool IsInternal { get; set; }
}
