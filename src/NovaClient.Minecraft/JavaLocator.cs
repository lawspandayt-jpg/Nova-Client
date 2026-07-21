using System.Diagnostics;
using System.Text.RegularExpressions;
using NovaClient.Core.Logging;

namespace NovaClient.Minecraft;

public sealed record JavaInstall(string ExecutablePath, string VersionString, int MajorVersion, bool Is64Bit)
{
    /// <summary>Compatible with Minecraft 1.8.9 (needs exactly 64-bit Java 8).</summary>
    public bool IsCompatible => Satisfies(8);

    /// <summary>
    /// Old versions (required 8) need exactly Java 8 — newer JVMs break 1.8.9-era code.
    /// Modern versions (required 16/17/21) accept their minimum or anything newer.
    /// </summary>
    public bool Satisfies(int requiredMajor) =>
        Is64Bit && (requiredMajor <= 8 ? MajorVersion == 8 : MajorVersion >= requiredMajor);

    public string Description => $"Java {VersionString} ({(Is64Bit ? "64-bit" : "32-bit")})";
}

/// <summary>Finds installed Java runtimes and validates them by running "java -version".</summary>
public static class JavaLocator
{
    public static async Task<List<JavaInstall>> DetectAllAsync(string? managedRuntimeDir = null)
    {
        var candidates = new List<string>();

        if (managedRuntimeDir is not null && Directory.Exists(managedRuntimeDir))
            candidates.AddRange(Directory.EnumerateFiles(managedRuntimeDir, "java.exe", SearchOption.AllDirectories));

        foreach (var root in new[]
                 {
                     @"C:\Program Files\Java",
                     @"C:\Program Files (x86)\Java",
                     @"C:\Program Files\Eclipse Adoptium",
                     @"C:\Program Files (x86)\Eclipse Adoptium",
                     @"C:\Program Files\Zulu",
                     @"C:\Program Files\Microsoft",
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Eclipse Adoptium"),
                 })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var exe = Path.Combine(dir, "bin", "java.exe");
                if (File.Exists(exe)) candidates.Add(exe);
            }
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var exe = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(exe)) candidates.Add(exe);
        }

        var results = new List<JavaInstall>();
        foreach (var exe in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var install = await ProbeAsync(exe);
            if (install is not null) results.Add(install);
        }
        NovaLog.Info("Java", $"Detected {results.Count} Java installation(s); {results.Count(r => r.IsCompatible)} compatible.");
        return results.OrderByDescending(r => r.IsCompatible).ThenByDescending(r => r.MajorVersion).ToList();
    }

    /// <summary>Runs "java -version" and parses the version + bitness. Null when the exe is not a JVM.</summary>
    public static async Task<JavaInstall?> ProbeAsync(string javaExe)
    {
        if (!File.Exists(javaExe)) return null;
        try
        {
            var psi = new ProcessStartInfo(javaExe, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var stderr = await process.StandardError.ReadToEndAsync();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync(new CancellationTokenSource(10_000).Token);
            var output = stderr + stdout;

            var match = Regex.Match(output, "version \"([^\"]+)\"");
            if (!match.Success) return null;
            var versionString = match.Groups[1].Value;
            var major = ParseMajor(versionString);
            var is64 = output.Contains("64-Bit", StringComparison.OrdinalIgnoreCase);
            return new JavaInstall(javaExe, versionString, major, is64);
        }
        catch (Exception ex)
        {
            NovaLog.Debug("Java", $"Probe failed for {javaExe}: {ex.Message}");
            return null;
        }
    }

    public static int ParseMajor(string versionString)
    {
        // "1.8.0_392" → 8;  "17.0.9" → 17;  "25" → 25
        var parts = versionString.Split('.', '_', '-', '+');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var first)) return 0;
        if (first == 1 && parts.Length > 1 && int.TryParse(parts[1], out var second)) return second;
        return first;
    }
}
