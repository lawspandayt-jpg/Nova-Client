namespace NovaClient.Core;

/// <summary>
/// Central directory layout. Everything lives under %AppData%\NovaClient — the player's normal
/// .minecraft folder is never read or written.
/// </summary>
public sealed class NovaPaths
{
    public string Root { get; }

    public NovaPaths(string? rootOverride = null)
    {
        Root = string.IsNullOrWhiteSpace(rootOverride)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovaClient")
            : rootOverride!;
    }

    public string Assets        => Path.Combine(Root, "assets");
    public string Libraries     => Path.Combine(Root, "libraries");
    public string Versions      => Path.Combine(Root, "versions");
    public string Natives       => Path.Combine(Root, "natives");
    public string Logs          => Path.Combine(Root, "logs");
    public string Screenshots   => Path.Combine(Root, "screenshots");
    public string ResourcePacks => Path.Combine(Root, "resourcepacks");
    public string Config        => Path.Combine(Root, "config");
    public string Cache         => Path.Combine(Root, "cache");
    public string JavaRuntime   => Path.Combine(Root, "runtime");
    public string ClientFiles   => Path.Combine(Root, "client");
    public string CrashReports  => Path.Combine(Root, "crash-reports");
    public string SecureStore   => Path.Combine(Config, "secure");

    public void EnsureCreated()
    {
        foreach (var dir in new[]
                 {
                     Root, Assets, Libraries, Versions, Natives, Logs, Screenshots, ResourcePacks,
                     Config, Cache, JavaRuntime, ClientFiles, CrashReports, SecureStore
                 })
        {
            Directory.CreateDirectory(dir);
        }
    }
}
