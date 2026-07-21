using System.IO.Compression;
using System.Text.Json;
using NovaClient.Core.Http;
using NovaClient.Core.Logging;
using NovaClient.Core.Util;

namespace NovaClient.Minecraft;

/// <summary>
/// Downloads a legally redistributable Eclipse Temurin (OpenJDK, x64, Windows) runtime of any
/// major version (8/17/21/…) from the official Adoptium API, verifies its published SHA-256,
/// and unpacks it into runtime/. Temurin is GPLv2+CE licensed; see THIRD-PARTY-NOTICES.md.
/// </summary>
public sealed class AdoptiumJavaProvider
{
    private readonly string _runtimeDirectory;
    private readonly string _cacheDirectory;
    private readonly int _majorVersion;

    public AdoptiumJavaProvider(string runtimeDirectory, string cacheDirectory, int majorVersion = 8)
    {
        _runtimeDirectory = runtimeDirectory;
        _cacheDirectory = cacheDirectory;
        _majorVersion = majorVersion;
    }

    private string ApiUrl =>
        $"https://api.adoptium.net/v3/assets/latest/{_majorVersion}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse";

    public async Task<JavaInstall> InstallAsync(IProgress<DownloadProgress>? progress, CancellationToken ct = default)
    {
        NovaLog.Info("Java", $"Querying Adoptium for the latest Temurin {_majorVersion} (x64) runtime…");
        var json = await HttpProvider.Client.GetStringAsync(ApiUrl, ct);
        using var doc = JsonDocument.Parse(json);
        var release = doc.RootElement.EnumerateArray().FirstOrDefault();
        if (release.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Adoptium returned no Temurin {_majorVersion} release for Windows x64.");

        var pkg = release.GetProperty("binary").GetProperty("package");
        var url = pkg.GetProperty("link").GetString()!;
        var checksum = pkg.GetProperty("checksum").GetString()!;
        var size = pkg.GetProperty("size").GetInt64();
        var releaseName = release.GetProperty("release_name").GetString() ?? "temurin-8";

        Directory.CreateDirectory(_cacheDirectory);
        var zipPath = Path.Combine(_cacheDirectory, releaseName + "-jre-x64.zip");

        var downloader = new DownloadService();
        await downloader.DownloadAllAsync(
            new[] { new DownloadItem(url, zipPath, Sha1: null, size) }, progress, ct);

        var actual = await HashUtil.Sha256FileAsync(zipPath, ct);
        if (!string.Equals(actual, checksum, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(zipPath);
            throw new InvalidOperationException("Downloaded Java runtime failed SHA-256 verification and was deleted.");
        }

        var targetDir = Path.Combine(_runtimeDirectory, releaseName);
        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, _runtimeDirectory, overwriteFiles: true);
        File.Delete(zipPath);

        var javaExe = Directory.EnumerateFiles(_runtimeDirectory, "java.exe", SearchOption.AllDirectories)
            .FirstOrDefault(p => p.Contains(releaseName, StringComparison.OrdinalIgnoreCase))
            ?? Directory.EnumerateFiles(_runtimeDirectory, "java.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("Extracted runtime contains no java.exe.");

        var install = await JavaLocator.ProbeAsync(javaExe)
                      ?? throw new InvalidOperationException("Extracted Java runtime failed validation.");
        NovaLog.Info("Java", $"Installed managed runtime: {install.Description}");
        return install;
    }
}
