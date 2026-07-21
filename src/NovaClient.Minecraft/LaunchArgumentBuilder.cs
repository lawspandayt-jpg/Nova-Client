using System.Text;
using System.Text.Json;
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
    /// <summary>Path to the nova-client jar, attached as a java agent (1.8.9 only).</summary>
    public string? JavaAgentPath { get; init; }

    /// <summary>-D properties (branding, config dir) forwarded to the in-game client.</summary>
    public IReadOnlyDictionary<string, string>? SystemProperties { get; init; }
}

/// <summary>
/// Builds the classpath and command line for both legacy ("minecraftArguments", ≤1.12.2) and
/// modern ("arguments" {game,jvm}, 1.13+) version JSONs, including inherited (Nova/Fabric)
/// versions. The access token is passed only to the local JVM and never logged.
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

    /// <summary>Merges a child version (nova-1.8.9 / fabric-loader-…) onto its vanilla parent.</summary>
    public static VersionJson Merge(VersionJson parent, VersionJson child)
    {
        ModernArguments? arguments = null;
        if (parent.Arguments is not null || child.Arguments is not null)
        {
            arguments = new ModernArguments();
            arguments.Game.AddRange(parent.Arguments?.Game ?? new List<JsonElement>());
            arguments.Game.AddRange(child.Arguments?.Game ?? new List<JsonElement>());
            arguments.Jvm.AddRange(parent.Arguments?.Jvm ?? new List<JsonElement>());
            arguments.Jvm.AddRange(child.Arguments?.Jvm ?? new List<JsonElement>());
        }
        return new VersionJson
        {
            Id = child.Id,
            MainClass = string.IsNullOrEmpty(child.MainClass) ? parent.MainClass : child.MainClass,
            MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments,
            Arguments = arguments,
            Assets = child.Assets ?? parent.Assets,
            AssetIndex = child.AssetIndex ?? parent.AssetIndex,
            Downloads = parent.Downloads,
            JavaVersion = parent.JavaVersion,
            // Child libraries first: loaders must precede the vanilla jar on the classpath.
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
        var substitutions = new Dictionary<string, string>
        {
            ["auth_player_name"] = options.Identity.Username,
            ["version_name"] = version.Id,
            ["game_directory"] = gameDirectory,
            ["assets_root"] = _paths.Assets,
            ["assets_index_name"] = version.AssetIndex?.Id ?? version.Assets ?? "legacy",
            ["auth_uuid"] = options.Identity.Uuid,
            ["auth_access_token"] = options.Identity.AccessToken,
            ["auth_session"] = options.Identity.AccessToken,
            ["auth_xuid"] = "0",
            ["clientid"] = "nova-client",
            ["user_properties"] = "{}",
            ["user_type"] = "msa",
            ["version_type"] = version.Type ?? "release",
            ["natives_directory"] = _installer.NativesDirectory,
            ["launcher_name"] = "NovaClient",
            ["launcher_version"] = "1.0",
            ["classpath"] = BuildClasspath(version),
        };

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
        };

        if (!string.IsNullOrEmpty(options.JavaAgentPath))
            args.Add($"-javaagent:{options.JavaAgentPath}");
        if (options.SystemProperties is not null)
            foreach (var (key, value) in options.SystemProperties)
                args.Add($"-D{key}={value}");
        if (!string.IsNullOrWhiteSpace(options.ExtraJvmArgs))
            args.AddRange(options.ExtraJvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (version.Arguments is not null)
        {
            // Modern: the version JSON supplies its own jvm args (including -cp ${classpath}).
            args.AddRange(ProcessModernList(version.Arguments.Jvm, substitutions));
            args.Add(version.MainClass);
            args.AddRange(ProcessModernList(version.Arguments.Game, substitutions));
        }
        else
        {
            args.Add($"-Djava.library.path={_installer.NativesDirectory}");
            args.Add("-cp");
            args.Add(substitutions["classpath"]);
            args.Add(version.MainClass);
            var template = version.MinecraftArguments
                           ?? "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userProperties ${user_properties} --userType ${user_type}";
            foreach (var token in template.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                args.Add(Substitute(token, substitutions));
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

    /// <summary>
    /// Expands a modern argument list: plain strings pass through (substituted); rule-guarded
    /// entries apply only when their OS rules match Windows; feature-guarded entries (demo mode,
    /// custom resolution, quick play) are skipped — the launcher appends its own equivalents.
    /// </summary>
    private static IEnumerable<string> ProcessModernList(List<JsonElement> list, Dictionary<string, string> substitutions)
    {
        foreach (var element in list)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                yield return Substitute(element.GetString()!, substitutions);
                continue;
            }
            if (element.ValueKind != JsonValueKind.Object) continue;
            if (!RulesAllow(element)) continue;

            if (!element.TryGetProperty("value", out var value)) continue;
            if (value.ValueKind == JsonValueKind.String)
            {
                yield return Substitute(value.GetString()!, substitutions);
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String)
                        yield return Substitute(item.GetString()!, substitutions);
            }
        }
    }

    private static bool RulesAllow(JsonElement entry)
    {
        if (!entry.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            return true;
        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            // Feature-conditional arguments are handled by the launcher itself.
            if (rule.TryGetProperty("features", out _)) return false;

            var matches = true;
            if (rule.TryGetProperty("os", out var os))
            {
                if (os.TryGetProperty("name", out var name) && name.GetString() != "windows") matches = false;
                if (os.TryGetProperty("arch", out var arch) && arch.GetString() != "x86" && arch.GetString() != "x64")
                {
                    // "x86" rules target 32-bit JVMs; we always launch 64-bit.
                    if (arch.GetString() == "x86") matches = false;
                }
            }
            var action = rule.TryGetProperty("action", out var a) ? a.GetString() : "allow";
            if (matches) allowed = action == "allow";
        }
        return allowed;
    }

    private static string Substitute(string input, Dictionary<string, string> substitutions)
    {
        if (!input.Contains("${")) return input;
        var result = input;
        foreach (var (key, value) in substitutions)
            result = result.Replace("${" + key + "}", value);
        return result;
    }

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
