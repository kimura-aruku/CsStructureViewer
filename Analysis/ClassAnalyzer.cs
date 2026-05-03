using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CsStructureViewer.Models;

namespace CsStructureViewer.Analysis;

// 型解決前の生の依存情報
internal record RawDependency(ClassNode Source, string TargetTypeName, bool IsBaseType);

public class ClassAnalyzer
{
    private readonly NamespaceResolver _resolver = new();

    internal (List<ClassNode> Nodes, List<RawDependency> RawDeps) Analyze(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var nodes = new List<ClassNode>();
        var rawDeps = new List<RawDependency>();

        var topLevelTypes = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(t => t.Parent is not TypeDeclarationSyntax);

        foreach (var typeDecl in topLevelTypes)
        {
            var node = BuildNode(typeDecl);
            nodes.Add(node);
            CollectRawDeps(typeDecl, node, rawDeps);
        }

        return (nodes, rawDeps);
    }

    private ClassNode BuildNode(TypeDeclarationSyntax typeDecl, string? inheritedNamespace = null)
    {
        var namespaceName = inheritedNamespace ?? _resolver.Resolve(typeDecl);
        var name = typeDecl.Identifier.Text;
        var node = new ClassNode
        {
            Name = name,
            FullyQualifiedName = namespaceName != null ? $"{namespaceName}.{name}" : name,
            NamespaceName = namespaceName,
            IsPartial = typeDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)),
            Kind = GetTypeKind(typeDecl)
        };

        foreach (var nested in typeDecl.Members.OfType<TypeDeclarationSyntax>())
            node.NestedClasses.Add(BuildNode(nested, namespaceName));

        return node;
    }

    private static void CollectRawDeps(
        TypeDeclarationSyntax typeDecl, ClassNode source, List<RawDependency> rawDeps)
    {
        if (typeDecl.BaseList != null)
        {
            foreach (var baseType in typeDecl.BaseList.Types)
                rawDeps.Add(new RawDependency(source, NormalizeTypeName(baseType.Type.ToString()), IsBaseType: true));
        }

        foreach (var field in typeDecl.Members.OfType<FieldDeclarationSyntax>())
            rawDeps.Add(new RawDependency(source, NormalizeTypeName(field.Declaration.Type.ToString()), IsBaseType: false));

        foreach (var prop in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
            rawDeps.Add(new RawDependency(source, NormalizeTypeName(prop.Type.ToString()), IsBaseType: false));
    }

    private static string NormalizeTypeName(string typeName)
    {
        typeName = typeName.TrimEnd('?');
        var idx = typeName.IndexOf('<');
        return (idx >= 0 ? typeName[..idx] : typeName).Trim();
    }

    private static Models.TypeKind GetTypeKind(TypeDeclarationSyntax typeDecl) => typeDecl switch
    {
        InterfaceDeclarationSyntax => Models.TypeKind.Interface,
        StructDeclarationSyntax => Models.TypeKind.Struct,
        RecordDeclarationSyntax => Models.TypeKind.Record,
        _ => Models.TypeKind.Class
    };
}
