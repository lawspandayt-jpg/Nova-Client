using System.Text.Json;
using NovaClient.Core.Logging;

namespace NovaClient.Core.Settings;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public LauncherSettings Current { get; private set; } = new();

    public SettingsService(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        _filePath = Path.Combine(configDirectory, "launcher-settings.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_filePath))
                Current = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_filePath)) ?? new LauncherSettings();
        }
        catch (Exception ex)
        {
            NovaLog.Warn("Settings", $"Failed to load settings, using defaults: {ex.Message}");
            Current = new LauncherSettings();
        }
        Current.RamMb = RamValidator.Clamp(Current.RamMb);
    }

    public void Save()
    {
        Current.RamMb = RamValidator.Clamp(Current.RamMb);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(Current, JsonOptions));
    }

    public void Reset()
    {
        Current = new LauncherSettings { RamMb = RamValidator.Recommended() };
        Save();
    }
}
