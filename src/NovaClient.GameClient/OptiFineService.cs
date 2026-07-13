using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using NovaClient.Core;
using NovaClient.Core.Logging;

namespace NovaClient.GameClient;

public sealed record OptiFineInfo(string Edition, string MinecraftVersion, string JarPath)
{
    public string LibraryName => $"optifine:OptiFine:{Edition}";
}

public enum OptiFineValidation
{
    Ok,
    FileMissing,
    NotAJar,
    NotOptiFine,
    WrongMinecraftVersion,
    Corrupted
}

/// <summary>
/// Legitimate OptiFine setup: the user supplies an official OptiFine 1.8.9 jar (downloaded from
/// optifine.net themselves — the jar is not redistributed by this project). The jar is validated
/// (real OptiFine tweaker, correct Minecraft version, readable zip) and copied into the local
/// libraries folder. 1.8.9 OptiFine ships complete classes, so no patching step is required —
/// it is loaded through its own LaunchWrapper tweaker (optifine.OptiFineTweaker), not Forge.
/// </summary>
public sealed class OptiFineService
{
    private readonly NovaPaths _paths;

    public OptiFineService(NovaPaths paths) => _paths = paths;

    private string OptiFineLibraryRoot => Path.Combine(_paths.Libraries, "optifine", "OptiFine");

    /// <summary>The currently installed OptiFine edition, if any.</summary>
    public OptiFineInfo? DetectInstalled()
    {
        if (!Directory.Exists(OptiFineLibraryRoot)) return null;
        foreach (var editionDir in Directory.EnumerateDirectories(OptiFineLibraryRoot))
        {
            var edition = Path.GetFileName(editionDir);
            var jar = Path.Combine(editionDir, $"OptiFine-{edition}.jar");
            if (File.Exists(jar) && Validate(jar, out var info) == OptiFineValidation.Ok)
                return info with { JarPath = jar };
        }
        return null;
    }

    /// <summary>Checks a user-selected jar. Reported problems map to clear UI messages.</summary>
    public OptiFineValidation Validate(string jarPath, out OptiFineInfo info)
    {
        info = new OptiFineInfo("", "", jarPath);
        if (!File.Exists(jarPath)) return OptiFineValidation.FileMissing;

        try
        {
            using var zip = ZipFile.OpenRead(jarPath);

            var tweaker = zip.GetEntry("optifine/OptiFineTweaker.class");
            if (tweaker is null) return OptiFineValidation.NotOptiFine;
            var config = zip.GetEntry("Config.class") ?? zip.GetEntry("optifine/Config.class");
            if (config is null) return OptiFineValidation.NotOptiFine;

            // The Config class constant pool contains the version markers, e.g.
            // "OptiFine_1.8.9_HD_U_M5" plus "1.8.9". Read it as raw bytes and scan for strings.
            string text;
            using (var stream = config.Open())
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                text = Encoding.ASCII.GetString(ms.ToArray());
            }

            var editionMatch = Regex.Match(text, @"OptiFine_(\d+\.\d+(?:\.\d+)?)_(HD_U_[A-Z]\d*)");
            string mcVersion, edition;
            if (editionMatch.Success)
            {
                mcVersion = editionMatch.Groups[1].Value;
                edition = $"{mcVersion}_{editionMatch.Groups[2].Value}";
            }
            else
            {
                // Fall back to the conventional file name: OptiFine_1.8.9_HD_U_M5.jar
                var nameMatch = Regex.Match(Path.GetFileName(jarPath), @"OptiFine[_-](\d+\.\d+(?:\.\d+)?)[_-](HD_U_[A-Z]\d*)", RegexOptions.IgnoreCase);
                if (!nameMatch.Success) return OptiFineValidation.NotOptiFine;
                mcVersion = nameMatch.Groups[1].Value;
                edition = $"{mcVersion}_{nameMatch.Groups[2].Value}";
            }

            if (mcVersion != "1.8.9") { info = new OptiFineInfo(edition, mcVersion, jarPath); return OptiFineValidation.WrongMinecraftVersion; }

            info = new OptiFineInfo(edition, mcVersion, jarPath);
            return OptiFineValidation.Ok;
        }
        catch (InvalidDataException)
        {
            return OptiFineValidation.Corrupted;
        }
        catch (Exception ex)
        {
            NovaLog.Warn("OptiFine", $"Validation error: {ex.Message}");
            return OptiFineValidation.Corrupted;
        }
    }

    /// <summary>Copies a validated jar into libraries/optifine/OptiFine/&lt;edition&gt;/.</summary>
    public OptiFineInfo Install(string jarPath)
    {
        var result = Validate(jarPath, out var info);
        if (result != OptiFineValidation.Ok)
            throw new InvalidOperationException($"OptiFine jar failed validation: {result}");

        var targetDir = Path.Combine(OptiFineLibraryRoot, info.Edition);
        Directory.CreateDirectory(targetDir);
        var target = Path.Combine(targetDir, $"OptiFine-{info.Edition}.jar");
        File.Copy(jarPath, target, overwrite: true);
        NovaLog.Info("OptiFine", $"Installed OptiFine {info.Edition}.");
        return info with { JarPath = target };
    }

    public void Uninstall()
    {
        if (Directory.Exists(OptiFineLibraryRoot))
            Directory.Delete(OptiFineLibraryRoot, recursive: true);
        NovaLog.Info("OptiFine", "OptiFine removed.");
    }

    public static string DescribeValidation(OptiFineValidation validation, OptiFineInfo info) => validation switch
    {
        OptiFineValidation.Ok => $"OptiFine {info.Edition} is valid and ready.",
        OptiFineValidation.FileMissing => "The selected OptiFine file no longer exists.",
        OptiFineValidation.NotAJar => "The selected file is not a jar file.",
        OptiFineValidation.NotOptiFine => "This file does not look like an official OptiFine jar (tweaker/Config classes missing).",
        OptiFineValidation.WrongMinecraftVersion => $"This OptiFine build is for Minecraft {info.MinecraftVersion}, but Nova Client needs an OptiFine 1.8.9 build (e.g. 1.8.9_HD_U_M5).",
        OptiFineValidation.Corrupted => "The OptiFine jar is corrupted and could not be read. Please download it again from optifine.net.",
        _ => "Unknown OptiFine validation result."
    };
}
