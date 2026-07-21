using System.Text.Json.Serialization;

namespace NovaClient.Core.Settings;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AfterLaunchBehavior { KeepOpen, Minimize, Close }

public sealed class LauncherSettings
{
    public int RamMb { get; set; } = 2048;
    public int WindowWidth { get; set; } = 854;
    public int WindowHeight { get; set; } = 480;
    public bool Fullscreen { get; set; }
    public string? JavaPath { get; set; }               // null → auto-detect
    public string? GameDirectory { get; set; }          // null → %AppData%\NovaClient
    public string ExtraJvmArgs { get; set; } = string.Empty;
    public AfterLaunchBehavior AfterLaunch { get; set; } = AfterLaunchBehavior.KeepOpen;
    public string SelectedVersion { get; set; } = "1.8.9";
    public bool UseFabric { get; set; }
    public bool RememberEmail { get; set; }
    public string RememberedEmail { get; set; } = string.Empty;   // an email address, not a secret
    public bool DisableDevUpdateChecks { get; set; }
}
