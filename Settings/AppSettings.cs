namespace CsStructureViewer.Settings;

public class AppSettings
{
    public List<string> ExcludePatterns { get; set; } = new();
    public List<string> InternalExcludePatterns { get; set; } = new();
    public List<ExcludePatternRule> ExcludePatternRules { get; set; } = new();
    public List<ExcludePatternRule> InternalExcludePatternRules { get; set; } = new();
    public bool DebugClassTransparencyEnabled { get; set; }
}

public class ExcludePatternRule
{
    public string Pattern { get; set; } = string.Empty;
    public bool MatchFolder { get; set; } = true;
    public bool MatchNamespace { get; set; } = true;
}
