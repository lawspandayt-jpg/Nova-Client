using System.Text;
using NovaClient.Core;

namespace NovaClient.Minecraft;

public sealed record LaunchIdentity(string Username, string Uuid, string AccessToken);

public sealed record LaunchOptions(
    LaunchIdentity Identity,
    int RamMb,
    int WindowWidth,
    int WindowHeight,
    bool Fullscreen,
    string ExtraJvmArgs)
{
    /// <summary>Path to the nova-client jar, attached as a java agent (LWJGL instrumentation).</summary>
    public string? JavaAgentPath { get; init; }

    /// <summary>-D properties (branding, config dir) forwarded to the in-game client.</summary>
    public IReadOnlyDictionary<string, string>? SystemProperties { get; init; }
}

/// <summary>
/// Builds the classpath and command line for (possibly inheriting) 1.8.9-era version JSONs.
/// The access token is passed as a process argument to the local JVM only — it is never logged
/// (see LogRedactor) and never sent anywhere except Mojang's own session services by the game.
/// </summary>
public sealed class LaunchArgumentBuilder
{
    private readonly NovaPaths _paths;
    private readonly MinecraftInstaller _installer;

    public LaunchArgumentBuilder(NovaPaths paths, MinecraftInstaller installer)
    {
        _paths = paths;
        _installer = installer;
    }

    /// <summary>Merges a child version (e.g. nova-1.8.9) onto its vanilla parent.</summary>
    public static VersionJson Merge(VersionJson parent, VersionJson child)
    {
        return new VersionJson
        {
            Id = child.Id,
            MainClass = string.IsNullOrEmpty(child.MainClass) ? parent.MainClass : child.MainClass,
            MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments,
            Assets = child.Assets ?? parent.Assets,
            AssetIndex = child.AssetIndex ?? parent.AssetIndex,
            Downloads = parent.Downloads,
            // Child libraries first: LaunchWrapper & co. must precede the vanilla jar on the classpath.
            Libraries = child.Libraries.Concat(parent.Libraries).ToList()
        };
    }

    public string BuildClasspath(VersionJson version)
    {
        var entries = new List<string>();
        foreach (var lib in version.Libraries.Where(l => l.AppliesToWindows()))
        {
            // Natives-only entries (classifier jars) are extracted, not put on the classpath.
            if (lib.Downloads is { Artifact: null, Classifiers: not null }) continue;
            var artifact = lib.Downloads?.Artifact;
            var path = artifact is not null
                ? _installer.LibraryPath(artifact, lib)
                : Path.Combine(_paths.Libraries, lib.MavenPath().Replace('/', Path.DirectorySeparatorChar));
            if (!entries.Contains(path, StringComparer.OrdinalIgnoreCase)) entries.Add(path);
        }
        entries.Add(_installer.ClientJarPath);
        return string.Join(';', entries);
    }

    public List<string> Build(VersionJson version, LaunchOptions options, string gameDirectory)
    {
        var args = new List<string>
        {
            $"-Xms{Math.Min(512, options.RamMb)}M",
            $"-Xmx{options.RamMb}M",
            "-XX:+UseG1GC",
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:G1NewSizePercent=20",
            "-XX:G1ReservePercent=20",
            "-XX:MaxGCPauseMillis=50",
            "-XX:G1HeapRegionSize=32M",
            $"-Djava.library.path={_installer.NativesDirectory}",
            "-Dfml.ignoreInvalidMinecraftCertificates=true",
        };

        if (!string.IsNullOrEmpty(options.JavaAgentPath))
            args.Add($"-javaagent:{options.JavaAgentPath}");
        if (options.SystemProperties is not null)
            foreach (var (key, value) in options.SystemProperties)
                args.Add($"-D{key}={value}");

        if (!string.IsNullOrWhiteSpace(options.ExtraJvmArgs))
            args.AddRange(options.ExtraJvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        args.Add("-cp");
        args.Add(BuildClasspath(version));
        args.Add(version.MainClass);

        var template = version.MinecraftArguments
                       ?? "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userProperties ${user_properties} --userType ${user_type}";

        var substitutions = new Dictionary<string, string>
        {
            ["auth_player_name"] = options.Identity.Username,
            ["version_name"] = version.Id,
            ["game_directory"] = gameDirectory,
            ["assets_root"] = _paths.Assets,
            ["assets_index_name"] = version.AssetIndex?.Id ?? version.Assets ?? "1.8",
            ["auth_uuid"] = options.Identity.Uuid,
            ["auth_access_token"] = options.Identity.AccessToken,
            ["user_properties"] = "{}",
            ["user_type"] = "msa",
            ["auth_session"] = options.Identity.AccessToken,
        };

        foreach (var token in Tokenize(template))
        {
            var value = token;
            if (token.StartsWith("${") && token.EndsWith("}"))
                value = substitutions.TryGetValue(token[2..^1], out var s) ? s : "";
            args.Add(value);
        }

        if (options.Fullscreen)
        {
            args.Add("--fullscreen");
        }
        else
        {
            args.Add("--width"); args.Add(options.WindowWidth.ToString());
            args.Add("--height"); args.Add(options.WindowHeight.ToString());
        }
        return args;
    }

    private static IEnumerable<string> Tokenize(string template) =>
        template.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Command line rendered for logs — the access token is replaced before formatting.</summary>
    public static string DescribeForLog(IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        string? previous = null;
        foreach (var arg in args)
        {
            sb.Append(previous == "--accessToken" ? "[REDACTED]" : arg).Append(' ');
            previous = arg;
        }
        return sb.ToString().TrimEnd();
    }
}
