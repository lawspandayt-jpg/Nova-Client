using System.Text.Json;
using System.Text.Json.Serialization;
using NovaClient.Core;
using NovaClient.Core.Logging;
using NovaClient.Minecraft;

namespace NovaClient.GameClient;

/// <summary>
/// Generates the custom "nova-1.8.9" version: vanilla 1.8.9 + LaunchWrapper bootstrap
/// (net.minecraft.launchwrapper.Launch) with the OptiFine tweaker (when installed) and the Nova
/// tweaker. No Forge, no Fabric — only Mojang's own LaunchWrapper plus our documented transformers.
/// </summary>
public sealed class NovaVersionService
{
    public const string VersionId = "nova-1.8.9";
    public const string NovaTweakClass = "dev.novaclient.bootstrap.NovaTweaker";
    public const string OptiFineTweakClass = "optifine.OptiFineTweaker";
    private const string MojangLibraries = "https://libraries.minecraft.net/";
    private const string MavenCentral = "https://repo1.maven.org/maven2/";

    private readonly NovaPaths _paths;
    private readonly string _clientVersion;

    public NovaVersionService(NovaPaths paths, string clientVersion)
    {
        _paths = paths;
        _clientVersion = clientVersion;
    }

    public string NovaClientLibraryPath =>
        Path.Combine(_paths.Libraries, "dev", "novaclient", "nova-client", _clientVersion,
            $"nova-client-{_clientVersion}.jar");

    /// <summary>
    /// Copies the nova-client jar (embedded-and-extracted, or shipped next to the exe) into the
    /// libraries folder. Returns false when no jar exists anywhere (client-java not built).
    /// </summary>
    public bool DeployNovaClientJar(params string[] candidatePaths)
    {
        var source = candidatePaths.FirstOrDefault(File.Exists);
        if (source is null)
        {
            NovaLog.Warn("Bootstrap", "nova-client.jar not found — build client-java first.");
            return File.Exists(NovaClientLibraryPath);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(NovaClientLibraryPath)!);
        File.Copy(source, NovaClientLibraryPath, overwrite: true);
        return true;
    }

    /// <summary>Downloads LaunchWrapper + ASM (from Mojang's own library server) if missing.</summary>
    public async Task EnsureBootstrapLibrariesAsync(IProgress<DownloadProgress>? progress, CancellationToken ct = default)
    {
        var items = new List<DownloadItem>();
        foreach (var (name, baseUrl, size) in new[]
                 {
                     ("net.minecraft:launchwrapper:1.12", MojangLibraries, 32999L),
                     ("org.ow2.asm:asm-debug-all:5.0.3", MavenCentral, 380792L),
                 })
        {
            var lib = new Library { Name = name };
            var relative = lib.MavenPath();
            var destination = Path.Combine(_paths.Libraries, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(destination))
                items.Add(new DownloadItem(baseUrl + relative, destination, Sha1: null, size));
        }
        if (items.Count > 0)
            await new DownloadService().DownloadAllAsync(items, progress, ct);
    }

    /// <summary>Writes versions/nova-1.8.9/nova-1.8.9.json and returns the parsed child version.</summary>
    public VersionJson WriteVersionJson(VersionJson vanilla, OptiFineInfo? optifine)
    {
        var tweaks = new List<string>();
        if (optifine is not null) tweaks.Add(OptiFineTweakClass);
        tweaks.Add(NovaTweakClass);
        var tweakArgs = string.Join(' ', tweaks.Select(t => $"--tweakClass {t}"));

        var libraries = new List<object>
        {
            new { name = "net.minecraft:launchwrapper:1.12", url = MojangLibraries },
            new { name = "org.ow2.asm:asm-debug-all:5.0.3", url = MavenCentral },
            new { name = $"dev.novaclient:nova-client:{_clientVersion}" },
        };
        if (optifine is not null)
            libraries.Add(new { name = optifine.LibraryName });

        var json = new
        {
            id = VersionId,
            inheritsFrom = MinecraftInstaller.VanillaVersion,
            type = "release",
            mainClass = "net.minecraft.launchwrapper.Launch",
            minecraftArguments = (vanilla.MinecraftArguments ?? "") + " " + tweakArgs,
            libraries
        };

        var dir = Path.Combine(_paths.Versions, VersionId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, VersionId + ".json");
        var text = JsonSerializer.Serialize(json, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        File.WriteAllText(path, text);
        NovaLog.Info("Bootstrap", $"Wrote {VersionId}.json (OptiFine: {(optifine is null ? "disabled" : optifine.Edition)}).");

        return JsonSerializer.Deserialize<VersionJson>(text)!;
    }
}
