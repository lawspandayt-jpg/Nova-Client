using NovaClient.Core;
using NovaClient.Core.Logging;
using NovaClient.Core.Settings;
using NovaClient.Minecraft;

namespace NovaClient.GameClient;

public sealed record PrepareResult(
    VersionJson MergedVersion,
    OptiFineInfo? OptiFine,
    bool NovaClientJarPresent,
    string VersionId,
    bool IsNovaVersion,
    string? FabricLoaderVersion)
{
    public int RequiredJavaMajor => MergedVersion.RequiredJavaMajor;
}

/// <summary>
/// End-to-end game pipeline: install/verify vanilla 1.8.9 → ensure LaunchWrapper + nova-client
/// libraries → generate the nova-1.8.9 version → build arguments → run the JVM.
/// </summary>
public sealed class NovaLauncher
{
    private readonly NovaPaths _paths;
    private readonly MinecraftInstaller _installer;
    private readonly NovaVersionService _versionService;
    private readonly OptiFineService _optiFine;
    private readonly FabricService _fabric;
    private readonly GameProcess _process;
    private readonly string _clientVersion;
    private string _clientName = "Nova Client";
    private string _accentColor = "#7C5CFF";

    /// <summary>Branding forwarded to the in-game client as -D properties.</summary>
    public void SetBranding(string clientName, string accentColor)
    {
        _clientName = clientName;
        _accentColor = accentColor;
    }

    public GameProcess Process => _process;
    public OptiFineService OptiFine => _optiFine;
    public MinecraftInstaller Installer => _installer;

    public NovaLauncher(NovaPaths paths, string clientVersion)
    {
        _paths = paths;
        _clientVersion = clientVersion;
        _installer = new MinecraftInstaller(paths);
        _versionService = new NovaVersionService(paths, clientVersion);
        _optiFine = new OptiFineService(paths);
        _fabric = new FabricService(paths);
        _process = new GameProcess(paths);
    }

    /// <summary>
    /// Installs/repairs everything required to launch the given version. 1.8.9 gets the Nova
    /// client (+ OptiFine when installed); newer versions launch vanilla or Fabric.
    /// Safe to run repeatedly.
    /// </summary>
    public async Task<PrepareResult> PrepareAsync(string versionId, bool useFabric,
        IProgress<InstallPhase>? progress, CancellationToken ct = default)
    {
        _installer.VersionId = versionId;
        var vanilla = await _installer.InstallAsync(progress, ct);
        var isNova = versionId == MinecraftInstaller.VanillaVersion;

        if (isNova)
        {
            progress?.Report(new InstallPhase("Preparing Nova bootstrap…", null));
            var bootstrapProgress = progress is null
                ? null
                : new Progress<DownloadProgress>(p => progress.Report(new InstallPhase("Downloading bootstrap libraries…", p)));
            await _versionService.EnsureBootstrapLibrariesAsync(bootstrapProgress, ct);

            var jarPresent = _versionService.DeployNovaClientJar(
                Path.Combine(_paths.ClientFiles, "nova-client.jar"),
                Path.Combine(AppContext.BaseDirectory, "client", "nova-client.jar"));
            var optifine = _optiFine.DetectInstalled();
            var child = _versionService.WriteVersionJson(vanilla, optifine);
            return new PrepareResult(LaunchArgumentBuilder.Merge(vanilla, child), optifine, jarPresent,
                versionId, IsNovaVersion: true, FabricLoaderVersion: null);
        }

        if (useFabric)
        {
            progress?.Report(new InstallPhase("Setting up Fabric…", null));
            var loader = await _fabric.GetLoaderVersionAsync(versionId, ct);
            if (loader is not null)
            {
                var fabricProgress = progress is null
                    ? null
                    : new Progress<DownloadProgress>(p => progress.Report(new InstallPhase("Downloading Fabric libraries…", p)));
                var profile = await _fabric.InstallAsync(versionId, loader, fabricProgress, ct);
                return new PrepareResult(LaunchArgumentBuilder.Merge(vanilla, profile), null,
                    NovaClientJarPresent: true, versionId, IsNovaVersion: false, FabricLoaderVersion: loader);
            }
            progress?.Report(new InstallPhase($"Fabric does not support {versionId} — launching vanilla.", null));
        }

        return new PrepareResult(vanilla, null, NovaClientJarPresent: true, versionId,
            IsNovaVersion: false, FabricLoaderVersion: null);
    }

    public async Task<GameExitInfo> LaunchAsync(
        PrepareResult prepared,
        LaunchIdentity identity,
        LauncherSettings settings,
        string javaExecutable,
        CancellationToken ct = default)
    {
        var builder = new LaunchArgumentBuilder(_paths, _installer);
        var options = new LaunchOptions(
            identity,
            RamValidator.Clamp(settings.RamMb),
            settings.WindowWidth,
            settings.WindowHeight,
            settings.Fullscreen,
            settings.ExtraJvmArgs)
        {
            // The Nova in-game client targets 1.8.9's engine (LWJGL 2) — never attach it elsewhere.
            JavaAgentPath = prepared.IsNovaVersion ? _versionService.NovaClientLibraryPath : null,
            SystemProperties = prepared.IsNovaVersion
                ? new Dictionary<string, string>
                {
                    ["nova.dir"] = _paths.Root,
                    ["nova.clientName"] = _clientName,
                    ["nova.clientVersion"] = _clientVersion,
                    ["nova.accentColor"] = _accentColor,
                }
                : null
        };
        _installer.VersionId = prepared.VersionId;
        var args = builder.Build(prepared.MergedVersion, options, _paths.Root);
        NovaLog.Info("Launcher", $"Starting Minecraft {prepared.MergedVersion.Id} as {identity.Username}.");
        return await _process.RunAsync(javaExecutable, args, ct);
    }
}
