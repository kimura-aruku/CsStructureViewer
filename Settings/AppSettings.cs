namespace CsStructureViewer.Settings;

public class AppSettings
{
    public List<string> ExcludePatterns { get; set; } = new();
    public List<string> InternalExcludePatterns { get; set; } = new();
}
