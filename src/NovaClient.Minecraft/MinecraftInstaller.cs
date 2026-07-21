using System.IO.Compression;
using System.Text.Json;
using NovaClient.Core;
using NovaClient.Core.Http;
using NovaClient.Core.Logging;
using NovaClient.Core.Util;

namespace NovaClient.Minecraft;

public sealed record InstallPhase(string Name, DownloadProgress? Download);

/// <summary>
/// Installs any official Minecraft release into %AppData%\NovaClient: version JSON, client jar,
/// libraries, natives (extracted), asset index and asset objects — all hash-verified against
/// Mojang's metadata. Repair simply re-runs the install; valid files are skipped by hash.
/// </summary>
public sealed class MinecraftInstaller
{
    public const string VanillaVersion = "1.8.9"; // the version the Nova in-game client targets
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string AssetBaseUrl = "https://resources.download.minecraft.net";

    private readonly NovaPaths _paths;
    private readonly DownloadService _downloader = new();

    public MinecraftInstaller(NovaPaths paths) => _paths = paths;

    /// <summary>The vanilla version currently being installed/launched (any release).</summary>
    public string VersionId { get; set; } = VanillaVersion;

    public string VersionDirectory => Path.Combine(_paths.Versions, VersionId);
    public string VersionJsonPath => Path.Combine(VersionDirectory, VersionId + ".json");
    public string ClientJarPath => Path.Combine(VersionDirectory, VersionId + ".jar");
    public string NativesDirectory => Path.Combine(_paths.Natives, VersionId);

    /// <summary>All release version ids from Mojang's manifest, newest first (cached copy offline).</summary>
    public async Task<List<string>> GetReleaseVersionsAsync(CancellationToken ct = default)
    {
        var cachePath = Path.Combine(_paths.Cache, "version_manifest_v2.json");
        string text;
        try
        {
            text = await Core.Http.HttpProvider.Client.GetStringAsync(ManifestUrl, ct);
            Directory.CreateDirectory(_paths.Cache);
            await File.WriteAllTextAsync(cachePath, text, ct);
        }
        catch (HttpRequestException) when (File.Exists(cachePath))
        {
            text = await File.ReadAllTextAsync(cachePath, ct);
        }
        var manifest = JsonSerializer.Deserialize<VersionManifest>(text)!;
        return manifest.Versions.Where(v => v.Type == "release").Select(v => v.Id).ToList();
    }

    public async Task<VersionJson> InstallAsync(IProgress<InstallPhase>? progress, CancellationToken ct = default)
    {
        _paths.EnsureCreated();

        progress?.Report(new InstallPhase("Fetching version manifest…", null));
        var version = await ResolveVersionJsonAsync(ct);

        progress?.Report(new InstallPhase("Collecting required files…", null));
        var items = new List<DownloadItem>();

        // Client jar
        if (version.Downloads is not null && version.Downloads.TryGetValue("client", out var client))
            items.Add(new DownloadItem(client.Url, ClientJarPath, client.Sha1, client.Size));

        // Libraries + natives
        var nativeArtifacts = new List<(ArtifactRef Artifact, ExtractRules? Extract)>();
        foreach (var lib in version.Libraries.Where(l => l.AppliesToWindows()))
        {
            var artifact = lib.Downloads?.Artifact;
            if (artifact is not null)
                items.Add(new DownloadItem(artifact.Url, LibraryPath(artifact, lib), artifact.Sha1, artifact.Size));

            if (lib.Natives is not null && lib.Natives.TryGetValue("windows", out var classifierKey))
            {
                classifierKey = classifierKey.Replace("${arch}", "64");
                if (lib.Downloads?.Classifiers is not null && lib.Downloads.Classifiers.TryGetValue(classifierKey, out var native))
                {
                    items.Add(new DownloadItem(native.Url, LibraryPath(native, lib, classifierKey), native.Sha1, native.Size));
                    nativeArtifacts.Add((native, lib.Extract));
                }
            }
        }

        // Asset index + objects
        var assetIndex = version.AssetIndex ?? throw new InvalidOperationException("Version JSON has no asset index.");
        var indexPath = Path.Combine(_paths.Assets, "indexes", assetIndex.Id + ".json");
        items.Add(new DownloadItem(assetIndex.Url, indexPath, assetIndex.Sha1, assetIndex.Size));

        progress?.Report(new InstallPhase("Downloading game files…", null));
        var downloadProgress = progress is null
            ? null
            : new Progress<DownloadProgress>(p => progress.Report(new InstallPhase("Downloading game files…", p)));
        await _downloader.DownloadAllAsync(items, downloadProgress, ct);

        // Assets are listed inside the (just downloaded) index.
        var index = JsonSerializer.Deserialize<AssetIndex>(await File.ReadAllTextAsync(indexPath, ct))
                    ?? throw new InvalidOperationException("Asset index could not be parsed.");
        var assetItems = index.Objects.Values
            .DistinctBy(o => o.Hash)
            .Select(o => new DownloadItem(
                $"{AssetBaseUrl}/{o.Hash[..2]}/{o.Hash}",
                Path.Combine(_paths.Assets, "objects", o.Hash[..2], o.Hash),
                o.Hash, o.Size))
            .ToList();

        progress?.Report(new InstallPhase("Downloading assets…", null));
        var assetProgress = progress is null
            ? null
            : new Progress<DownloadProgress>(p => progress.Report(new InstallPhase("Downloading assets…", p)));
        await _downloader.DownloadAllAsync(assetItems, assetProgress, ct);

        progress?.Report(new InstallPhase("Extracting native libraries…", null));
        ExtractNatives(nativeArtifacts, version);

        NovaLog.Info("Install", $"Minecraft {VersionId} installation verified ({items.Count} core files, {assetItems.Count} assets).");
        return version;
    }

    private async Task<VersionJson> ResolveVersionJsonAsync(CancellationToken ct)
    {
        string versionJsonText;
        try
        {
            var manifestText = await HttpProvider.Client.GetStringAsync(ManifestUrl, ct);
            var manifest = JsonSerializer.Deserialize<VersionManifest>(manifestText)!;
            var entry = manifest.Versions.FirstOrDefault(v => v.Id == VersionId)
                        ?? throw new InvalidOperationException($"Minecraft {VersionId} not found in Mojang's manifest.");
            versionJsonText = await HttpProvider.Client.GetStringAsync(entry.Url, ct);
            Directory.CreateDirectory(VersionDirectory);
            await File.WriteAllTextAsync(VersionJsonPath, versionJsonText, ct);
        }
        catch (HttpRequestException ex)
        {
            // Offline: fall back to a previously downloaded version JSON if we have one.
            if (!File.Exists(VersionJsonPath))
                throw new IOException("Cannot download Minecraft metadata and no cached copy exists. Check your internet connection.", ex);
            NovaLog.Warn("Install", "Using cached version JSON (Mojang metadata unreachable).");
            versionJsonText = await File.ReadAllTextAsync(VersionJsonPath, ct);
        }
        return JsonSerializer.Deserialize<VersionJson>(versionJsonText)
               ?? throw new InvalidOperationException("Version JSON could not be parsed.");
    }

    public string LibraryPath(ArtifactRef artifact, Library lib, string? classifier = null)
    {
        var relative = artifact.Path ?? lib.MavenPath(classifier);
        return Path.Combine(_paths.Libraries, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private void ExtractNatives(List<(ArtifactRef Artifact, ExtractRules? Extract)> natives, VersionJson version)
    {
        Directory.CreateDirectory(NativesDirectory);
        foreach (var (artifact, extract) in natives)
        {
            var jarPath = Path.Combine(_paths.Libraries, (artifact.Path ?? "").Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(jarPath)) continue;
            using var zip = ZipFile.OpenRead(jarPath);
            foreach (var entry in zip.Entries)
            {
                if (entry.Name.Length == 0) continue;
                var excluded = extract?.Exclude?.Any(prefix => entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) ?? false;
                if (excluded || entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(NativesDirectory, entry.Name);
                try { entry.ExtractToFile(target, overwrite: true); }
                catch (IOException) { /* file locked by a running game â€” keep existing */ }
            }
        }
    }

    /// <summary>Deletes cached downloads (never settings, screenshots, or resource packs).</summary>
    public void ClearDownloadCache()
    {
        foreach (var part in Directory.EnumerateFiles(_paths.Root, "*.part", SearchOption.AllDirectories).ToList())
            File.Delete(part);
        if (Directory.Exists(_paths.Cache))
        {
            Directory.Delete(_paths.Cache, recursive: true);
            Directory.CreateDirectory(_paths.Cache);
        }
    }
}

