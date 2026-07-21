using System.Text.Json;
using NovaClient.Core;
using NovaClient.Core.Http;
using NovaClient.Core.Logging;
using NovaClient.Minecraft;

namespace NovaClient.GameClient;

/// <summary>
/// Fabric loader integration for modern versions, using Fabric's own public meta API — the
/// supported, automation-friendly way to install Fabric (unlike OptiFine, redistribution and
/// automated setup are explicitly permitted by the Fabric project).
/// </summary>
public sealed class FabricService
{
    private const string MetaBase = "https://meta.fabricmc.net/v2/versions/loader/";

    private readonly NovaPaths _paths;

    public FabricService(NovaPaths paths) => _paths = paths;

    /// <summary>Latest stable loader version for the given Minecraft version, null if unsupported.</summary>
    public async Task<string?> GetLoaderVersionAsync(string gameVersion, CancellationToken ct = default)
    {
        try
        {
            var json = await HttpProvider.Client.GetStringAsync(MetaBase + Uri.EscapeDataString(gameVersion), ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var loader = entry.GetProperty("loader");
                var stable = loader.TryGetProperty("stable", out var s) && s.GetBoolean();
                if (stable) return loader.GetProperty("version").GetString();
            }
            var first = doc.RootElement.EnumerateArray().FirstOrDefault();
            return first.ValueKind == JsonValueKind.Object
                ? first.GetProperty("loader").GetProperty("version").GetString()
                : null;
        }
        catch (Exception ex)
        {
            NovaLog.Warn("Fabric", $"Loader lookup for {gameVersion} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads Fabric's launcher profile for the version, saves it under versions/, downloads
    /// the loader libraries (Fabric maven), and returns the parsed child version JSON.
    /// </summary>
    public async Task<VersionJson> InstallAsync(string gameVersion, string loaderVersion,
        IProgress<DownloadProgress>? progress, CancellationToken ct = default)
    {
        var url = $"{MetaBase}{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";
        var text = await HttpProvider.Client.GetStringAsync(url, ct);
        var profile = JsonSerializer.Deserialize<VersionJson>(text)
                      ?? throw new InvalidOperationException("Fabric profile JSON could not be parsed.");

        var dir = Path.Combine(_paths.Versions, profile.Id);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, profile.Id + ".json"), text, ct);

        // Fabric libraries are Maven-style entries with a base "url" (maven.fabricmc.net / central).
        var items = new List<DownloadItem>();
        foreach (var lib in profile.Libraries.Where(l => l.AppliesToWindows()))
        {
            var relative = lib.Downloads?.Artifact?.Path ?? lib.MavenPath();
            var destination = Path.Combine(_paths.Libraries, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(destination)) continue;
            var baseUrl = lib.Downloads?.Artifact?.Url;
            if (string.IsNullOrEmpty(baseUrl))
                baseUrl = (lib.MavenBaseUrl ?? "https://maven.fabricmc.net/").TrimEnd('/') + "/" + lib.MavenPath();
            items.Add(new DownloadItem(baseUrl!, destination, lib.Downloads?.Artifact?.Sha1, lib.Downloads?.Artifact?.Size ?? 0));
        }
        if (items.Count > 0)
            await new DownloadService().DownloadAllAsync(items, progress, ct);

        NovaLog.Info("Fabric", $"Fabric {loaderVersion} ready for Minecraft {gameVersion}.");
        return profile;
    }
}
