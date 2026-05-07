using System.IO;
using System.Text.Json;

namespace CsStructureViewer.Settings;

public class SettingsManager
{
    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CsStructureViewer");

    private static readonly string SettingsFilePath =
        Path.Combine(SettingsDirectory, "settings.json");

    private static readonly List<string> DefaultExcludePatterns =
    [
        "bin", "obj", ".git", "Editor", "Temp", "temp", "Tests"
    ];

    private static readonly List<string> DefaultInternalExcludePatterns =
    [
        "Library"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
            return CreateDefault();

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefault();
            Normalize(settings);
            return settings;
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }

    private AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.ExcludePatterns.AddRange(DefaultExcludePatterns);
        settings.InternalExcludePatterns.AddRange(DefaultInternalExcludePatterns);
        settings.ExcludePatternRules.AddRange(DefaultExcludePatterns.Select(CreateRule));
        settings.InternalExcludePatternRules.AddRange(DefaultInternalExcludePatterns.Select(CreateRule));
        Save(settings);
        return settings;
    }

    private static void Normalize(AppSettings settings)
    {
        if (settings.ExcludePatternRules.Count == 0 && settings.ExcludePatterns.Count > 0)
            settings.ExcludePatternRules.AddRange(settings.ExcludePatterns.Select(CreateRule));

        if (settings.InternalExcludePatternRules.Count == 0 && settings.InternalExcludePatterns.Count > 0)
            settings.InternalExcludePatternRules.AddRange(settings.InternalExcludePatterns.Select(CreateRule));

        settings.ExcludePatterns = settings.ExcludePatternRules.Select(r => r.Pattern).ToList();
        settings.InternalExcludePatterns = settings.InternalExcludePatternRules.Select(r => r.Pattern).ToList();
    }

    private static ExcludePatternRule CreateRule(string pattern) =>
        new()
        {
            Pattern = pattern,
            MatchFolder = true,
            MatchNamespace = true
        };
}
