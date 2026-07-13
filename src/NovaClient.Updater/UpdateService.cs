using System.Text.Json;
using System.Text.Json.Serialization;
using NovaClient.Core.Http;
using NovaClient.Core.Logging;
using NovaClient.Core.Util;

namespace NovaClient.Updater;

public sealed class UpdateManifest
{
    [JsonPropertyName("launcherVersion")] public string LauncherVersion { get; set; } = "";
    [JsonPropertyName("clientVersion")] public string ClientVersion { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("files")] public List<UpdateFile> Files { get; set; } = new();
}

public sealed class UpdateFile
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";      // relative to install dir
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

public sealed record UpdateCheckResult(bool UpdateAvailable, string CurrentVersion, string LatestVersion, string Notes, UpdateManifest? Manifest);

/// <summary>
/// Safe self-update pipeline: HTTPS manifest → semver compare → download to a temp staging folder
/// → SHA-256 verify every file → back up current files → apply → roll back on any failure.
/// User data (settings, screenshots, resource packs, logs) is never touched: updates only ever
/// replace files listed in the manifest, which live in the install directory.
/// </summary>
public sealed class UpdateService
{
    private readonly string _manifestUrl;
    private readonly string _installDirectory;
    private readonly string _stagingRoot;

    public UpdateService(string manifestUrl, string installDirectory, string cacheDirectory)
    {
        _manifestUrl = manifestUrl;
        _installDirectory = installDirectory;
        _stagingRoot = Path.Combine(cacheDirectory, "updates");
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        if (!_manifestUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !_manifestUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update manifest URL must use HTTPS (or file:// for local development).");

        string text;
        if (_manifestUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            text = await File.ReadAllTextAsync(new Uri(_manifestUrl).LocalPath, ct);
        else
            text = await HttpProvider.Client.GetStringAsync(_manifestUrl, ct);

        var manifest = JsonSerializer.Deserialize<UpdateManifest>(text)
                       ?? throw new InvalidOperationException("Update manifest could not be parsed.");

        if (!SemVersion.TryParse(currentVersion, out var current) ||
            !SemVersion.TryParse(manifest.LauncherVersion, out var latest))
            throw new InvalidOperationException("Update manifest contains an invalid version.");

        var available = latest.CompareTo(current) > 0;
        NovaLog.Info("Updater", available
            ? $"Update available: {current} → {latest}."
            : $"Launcher is up to date ({current}).");
        return new UpdateCheckResult(available, current.ToString(), latest.ToString(), manifest.Notes, manifest);
    }

    public async Task DownloadAndApplyAsync(UpdateManifest manifest, IProgress<(string File, double Fraction)>? progress, CancellationToken ct = default)
    {
        if (manifest.Files.Count == 0) throw new InvalidOperationException("Update manifest lists no files.");
        foreach (var f in manifest.Files)
            if (f.Path.Contains("..") || Path.IsPathRooted(f.Path))
                throw new InvalidOperationException($"Update manifest contains an unsafe path: {f.Path}");

        var staging = Path.Combine(_stagingRoot, manifest.LauncherVersion);
        var backup = Path.Combine(_stagingRoot, "backup-" + manifest.LauncherVersion);
        Directory.CreateDirectory(staging);

        // 1) Download everything into staging and verify SHA-256 before touching the install.
        for (var i = 0; i < manifest.Files.Count; i++)
        {
            var file = manifest.Files[i];
            var target = Path.Combine(staging, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            progress?.Report((file.Path, (double)i / manifest.Files.Count));

            var bytes = await HttpProvider.Client.GetByteArrayAsync(file.Url, ct);
            await File.WriteAllBytesAsync(target, bytes, ct);

            var hash = await HashUtil.Sha256FileAsync(target, ct);
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"'{file.Path}' failed SHA-256 verification; update aborted before installation.");
        }

        // 2) Back up current files, then apply. Roll back everything on any failure.
        Directory.CreateDirectory(backup);
        var applied = new List<string>();
        try
        {
            foreach (var file in manifest.Files)
            {
                var installPath = Path.Combine(_installDirectory, file.Path);
                var backupPath = Path.Combine(backup, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                if (File.Exists(installPath)) File.Copy(installPath, backupPath, overwrite: true);

                Directory.CreateDirectory(Path.GetDirectoryName(installPath)!);
                File.Copy(Path.Combine(staging, file.Path), installPath, overwrite: true);
                applied.Add(file.Path);
            }
            progress?.Report(("done", 1.0));
            NovaLog.Info("Updater", $"Update {manifest.LauncherVersion} applied ({applied.Count} files).");
        }
        catch (Exception ex)
        {
            NovaLog.Error("Updater", $"Update failed after {applied.Count} file(s); rolling back", ex);
            foreach (var path in applied)
            {
                var backupPath = Path.Combine(backup, path);
                var installPath = Path.Combine(_installDirectory, path);
                try
                {
                    if (File.Exists(backupPath)) File.Copy(backupPath, installPath, overwrite: true);
                    else File.Delete(installPath);
                }
                catch (Exception rollbackEx)
                {
                    NovaLog.Error("Updater", $"Rollback of '{path}' failed", rollbackEx);
                }
            }
            throw;
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    /// <summary>Writes a local manifest for development testing (docs/update-testing.md).</summary>
    public static async Task WriteDevManifestAsync(string path, string launcherVersion, string clientVersion, IEnumerable<(string RelPath, string Url, string FullPath)> files)
    {
        var manifest = new UpdateManifest { LauncherVersion = launcherVersion, ClientVersion = clientVersion, Notes = "Local development manifest." };
        foreach (var (rel, url, full) in files)
        {
            manifest.Files.Add(new UpdateFile
            {
                Path = rel,
                Url = url,
                Sha256 = await HashUtil.Sha256FileAsync(full),
                Size = new FileInfo(full).Length
            });
        }
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}
