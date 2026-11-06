using System.Text.Json;

namespace Bke.MetadataManager.Services;

public sealed class AppSettings
{
    public string DestinationFolder { get; set; } = "";
    public List<string> Categories { get; set; } = ["Establishing", "Detail", "Drone"];
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public string FilePath { get; }

    public SettingsStore(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BKE", "MediaOrganizer");
        FilePath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions)
            ?? throw new InvalidDataException("Settings JSON was empty.");
        settings.Categories ??= [];
        settings.Categories = settings.Categories
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(PathNames.Segment)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
