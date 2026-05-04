using System.IO;
using CsStructureViewer.Models;
using CsStructureViewer.Settings;
using Microsoft.CodeAnalysis.CSharp;

namespace CsStructureViewer.Analysis;

public class ProjectAnalyzer
{
    private readonly ClassAnalyzer _classAnalyzer = new();

    public async Task<ProjectGraph> AnalyzeAsync(
        string projectPath,
        AppSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = GetCsFiles(projectPath, settings.ExcludePatterns).ToList();

        var allNodes = new List<ClassNode>();
        var allRawDeps = new List<RawDependency>();
        var internalNamespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(Path.GetFileName(file));

            var code = await File.ReadAllTextAsync(file, cancellationToken);
            var tree = CSharpSyntaxTree.ParseText(code, cancellationToken: cancellationToken);
            var (nodes, rawDeps) = _classAnalyzer.Analyze(tree);

            if (settings.InternalExcludePatterns.Any(p => IsInMatchingFolder(file, p)))
                foreach (var node in nodes)
                    if (node.NamespaceName != null)
                        internalNamespaces.Add(node.NamespaceName);

            allNodes.AddRange(nodes);
            allRawDeps.AddRange(rawDeps);
        }

        var mergedNodes = MergePartialClasses(allNodes);
        var lookup = BuildLookup(mergedNodes);
        var edges = ResolveEdges(allRawDeps, lookup);

        return BuildGraph(mergedNodes, edges, internalNamespaces);
    }

    private static bool IsInMatchingFolder(string filePath, string pattern) =>
        !string.IsNullOrEmpty(pattern) &&
        filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(seg => seg.Equals(pattern, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> GetCsFiles(string rootPath, List<string> excludePatterns)
    {
        return Directory.EnumerateFiles(rootPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !excludePatterns.Any(p =>
                f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(seg => seg.Equals(p, StringComparison.OrdinalIgnoreCase))));
    }

    private static List<ClassNode> MergePartialClasses(List<ClassNode> nodes)
    {
        var merged = new Dictionary<string, ClassNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (merged.TryGetValue(node.FullyQualifiedName, out var existing))
            {
                foreach (var nested in node.NestedClasses)
                    if (!existing.NestedClasses.Any(n => n.FullyQualifiedName == nested.FullyQualifiedName))
                        existing.NestedClasses.Add(nested);
            }
            else
            {
                merged[node.FullyQualifiedName] = node;
            }
        }
        return [.. merged.Values];
    }

    private static Dictionary<string, ClassNode> BuildLookup(List<ClassNode> nodes)
    {
        var lookup = new Dictionary<string, ClassNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            lookup[node.FullyQualifiedName] = node;
            lookup.TryAdd(node.Name, node);
        }
        return lookup;
    }

    private static List<DependencyEdge> ResolveEdges(
        List<RawDependency> rawDeps, Dictionary<string, ClassNode> lookup)
    {
        var edges = new List<DependencyEdge>();
        var seen = new HashSet<(ClassNode, ClassNode, DependencyKind)>();

        foreach (var raw in rawDeps)
        {
            if (!lookup.TryGetValue(raw.TargetTypeName, out var target)) continue;
            if (raw.Source == target) continue;

            var kind = raw.IsBaseType
                ? (target.Kind == TypeKind.Interface ? DependencyKind.Implementation : DependencyKind.Inheritance)
                : DependencyKind.FieldReference;

            if (seen.Add((raw.Source, target, kind)))
                edges.Add(new DependencyEdge { Source = raw.Source, Target = target, Kind = kind });
        }

        return edges;
    }

    private static ProjectGraph BuildGraph(
        List<ClassNode> nodes, List<DependencyEdge> edges, HashSet<string> internalNamespaces)
    {
        var graph = new ProjectGraph();

        foreach (var group in nodes.Where(n => n.NamespaceName != null).GroupBy(n => n.NamespaceName!))
        {
            var ns = new NamespaceNode
            {
                Name = group.Key,
                IsInternal = internalNamespaces.Contains(group.Key)
            };
            ns.Classes.AddRange(group);
            graph.Namespaces.Add(ns);
        }

        graph.GlobalClasses.AddRange(nodes.Where(n => n.NamespaceName == null));
        graph.Edges.AddRange(edges);

        return graph;
    }
}
