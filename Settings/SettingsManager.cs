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

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
            return CreateDefault();

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }

    private AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.ExcludePatterns.AddRange(DefaultExcludePatterns);
        Save(settings);
        return settings;
    }
}
