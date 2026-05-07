using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsStructureViewer.Analysis;

public class NamespaceResolver
{
    public string? Resolve(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is NamespaceDeclarationSyntax ns)
                return ns.Name.ToString();
            if (parent is FileScopedNamespaceDeclarationSyntax fsNs)
                return fsNs.Name.ToString();
            parent = parent.Parent;
        }
        return null;
    }
}
