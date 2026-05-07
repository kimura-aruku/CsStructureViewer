using System.IO;
using System.Text.Json;
using CsStructureViewer.Layout;

namespace CsStructureViewer.Diagnostics;

public static class LayoutDiagnosticsWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string WriteLatest(LayoutResult result, string projectPath)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "layout-diagnostics");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "latest-layout.json");
        var payload = BuildPayload(result, projectPath);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, Options));
        return path;
    }

    private static object BuildPayload(LayoutResult result, string projectPath)
    {
        var arrows = result.Arrows.Select((arrow, index) =>
        {
            var segments = arrow.Segments.Select((segment, segmentIndex) => new
            {
                index = segmentIndex,
                start = PointDto(segment.Start),
                end = PointDto(segment.End),
                direction = segment.Direction.ToString(),
                length = Round(segment.Length)
            }).ToList();

            return new
            {
                index,
                source = arrow.SourceLabel,
                target = arrow.TargetLabel,
                kind = arrow.Kind.ToString(),
                sourceSide = arrow.SourceSide,
                targetSide = arrow.TargetSide,
                expectedFirstDirection = ExpectedSourceDirection(arrow.SourceSide),
                actualFirstDirection = arrow.Segments.FirstOrDefault()?.Direction.ToString(),
                expectedLastDirection = ExpectedTargetDirection(arrow.TargetSide),
                actualLastDirection = arrow.Segments.LastOrDefault()?.Direction.ToString(),
                start = PointDto(arrow.Start),
                end = PointDto(arrow.End),
                segments,
                rendered = new
                {
                    shaftPoints = arrow.RenderedShaftPoints.Select(PointDto).ToList(),
                    headPoints = arrow.RenderedHeadPoints.Select(PointDto).ToList()
                },
                issues = GetArrowIssues(arrow, result.Arrows, index)
                    .Concat(GetObstacleIssues(arrow, result, index))
                    .Distinct()
                    .ToList()
            };
        }).ToList();

        return new
        {
            generatedAt = DateTimeOffset.Now,
            projectPath,
            namespaceRects = result.NamespaceRects.Select(kv => new
            {
                name = kv.Key.Name,
                isFolder = result.FolderNamespaces.Contains(kv.Key),
                rect = RectDto(kv.Value)
            }),
            classRects = result.ClassRects.Select(kv => new
            {
                name = kv.Key.FullyQualifiedName,
                rect = RectDto(kv.Value)
            }),
            arrows
        };
    }

    private static List<string> GetArrowIssues(ArrowRoute arrow, IReadOnlyList<ArrowRoute> allArrows, int index)
    {
        var issues = new List<string>();

        var first = arrow.Segments.FirstOrDefault();
        var last = arrow.Segments.LastOrDefault();
        var expectedFirst = ExpectedSourceDirection(arrow.SourceSide);
        var expectedLast = ExpectedTargetDirection(arrow.TargetSide);

        if (first != null && expectedFirst != null && first.Direction.ToString() != expectedFirst)
            issues.Add($"Source side mismatch: expected {expectedFirst}, actual {first.Direction}.");

        if (last != null && expectedLast != null && last.Direction.ToString() != expectedLast)
            issues.Add($"Target side mismatch: expected {expectedLast}, actual {last.Direction}.");

        for (var i = 1; i < arrow.Segments.Count; i++)
        {
            if (!PointsClose(arrow.Segments[i - 1].End, arrow.Segments[i].Start))
                issues.Add($"Disconnected segment: {i - 1} -> {i}.");
        }

        for (var i = 0; i < arrow.Segments.Count; i++)
        {
            for (var j = i + 1; j < arrow.Segments.Count; j++)
            {
                if (SegmentsOverlap(arrow.Segments[i], arrow.Segments[j]))
                    issues.Add($"Self-overlap: segment[{i}] overlaps segment[{j}].");
            }
        }

        for (var otherIndex = 0; otherIndex < allArrows.Count; otherIndex++)
        {
            if (otherIndex == index) continue;
            foreach (var segment in arrow.Segments)
            {
                if (allArrows[otherIndex].Segments.Any(other => SegmentsOverlap(segment, other)))
                {
                    issues.Add($"Overlaps arrow[{otherIndex}] {allArrows[otherIndex].SourceLabel} -> {allArrows[otherIndex].TargetLabel}.");
                    break;
                }
            }
        }

        return issues.Distinct().ToList();
    }

    private static List<string> GetObstacleIssues(ArrowRoute arrow, LayoutResult result, int index)
    {
        var issues = new List<string>();
        foreach (var kv in result.ClassRects)
        {
            var key = $"class:{kv.Key.FullyQualifiedName}";
            if (key == arrow.SourceKey || key == arrow.TargetKey)
                continue;

            if (arrow.Segments.Any(segment => SegmentIntersectsRect(segment, kv.Value)))
                issues.Add($"Intersects class rect: {kv.Key.FullyQualifiedName}.");
        }

        foreach (var kv in result.NamespaceRects)
        {
            var key = $"namespace:{kv.Key.Name}";
            if (key == arrow.SourceKey || key == arrow.TargetKey)
                continue;

            if (arrow.Segments.Any(segment => SegmentIntersectsRect(segment, kv.Value)))
                issues.Add($"Intersects namespace rect: {kv.Key.Name}.");
        }

        return issues;
    }

    private static string? ExpectedSourceDirection(string side) => side switch
    {
        "Left" => Direction.Left.ToString(),
        "Right" => Direction.Right.ToString(),
        "Top" => Direction.Up.ToString(),
        "Bottom" => Direction.Down.ToString(),
        _ => null
    };

    private static string? ExpectedTargetDirection(string side) => side switch
    {
        "Left" => Direction.Right.ToString(),
        "Right" => Direction.Left.ToString(),
        "Top" => Direction.Down.ToString(),
        "Bottom" => Direction.Up.ToString(),
        _ => null
    };

    private static object PointDto(System.Windows.Point point) => new
    {
        x = Round(point.X),
        y = Round(point.Y)
    };

    private static object RectDto(System.Windows.Rect rect) => new
    {
        x = Round(rect.X),
        y = Round(rect.Y),
        width = Round(rect.Width),
        height = Round(rect.Height),
        left = Round(rect.Left),
        right = Round(rect.Right),
        top = Round(rect.Top),
        bottom = Round(rect.Bottom)
    };

    private static bool SegmentsOverlap(RouteSegment a, RouteSegment b)
    {
        var aHoriz = a.Direction is Direction.Right or Direction.Left;
        var bHoriz = b.Direction is Direction.Right or Direction.Left;
        if (aHoriz != bHoriz) return false;

        if (aHoriz)
        {
            if (Math.Abs(a.Y - b.Y) >= 0.1) return false;
            return RangesOverlap(a.X, a.End.X, b.X, b.End.X);
        }

        if (Math.Abs(a.X - b.X) >= 0.1) return false;
        return RangesOverlap(a.Y, a.End.Y, b.Y, b.End.Y);
    }

    private static bool SegmentIntersectsRect(RouteSegment segment, System.Windows.Rect rect)
    {
        var isHoriz = segment.Direction is Direction.Right or Direction.Left;
        if (isHoriz)
        {
            var y = segment.Y;
            if (y <= rect.Top || y >= rect.Bottom) return false;
            var minX = Math.Min(segment.X, segment.End.X);
            var maxX = Math.Max(segment.X, segment.End.X);
            return maxX > rect.Left && minX < rect.Right;
        }

        var x = segment.X;
        if (x <= rect.Left || x >= rect.Right) return false;
        var minY = Math.Min(segment.Y, segment.End.Y);
        var maxY = Math.Max(segment.Y, segment.End.Y);
        return maxY > rect.Top && minY < rect.Bottom;
    }

    private static bool RangesOverlap(double a1, double a2, double b1, double b2)
    {
        var minA = Math.Min(a1, a2);
        var maxA = Math.Max(a1, a2);
        var minB = Math.Min(b1, b2);
        var maxB = Math.Max(b1, b2);
        return Math.Min(maxA, maxB) - Math.Max(minA, minB) > 0.1;
    }

    private static bool PointsClose(System.Windows.Point a, System.Windows.Point b) =>
        Math.Abs(a.X - b.X) < 0.1 && Math.Abs(a.Y - b.Y) < 0.1;

    private static double Round(double value) => Math.Round(value, 2);
}
