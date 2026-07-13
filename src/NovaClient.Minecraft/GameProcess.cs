using System.Diagnostics;
using NovaClient.Core;
using NovaClient.Core.Logging;

namespace NovaClient.Minecraft;

public sealed record GameExitInfo(int ExitCode, bool Crashed, string? CrashReportPath, string GameLogPath);

/// <summary>
/// Runs the Minecraft JVM, streams its output into a per-session game log, and detects crashes
/// (non-zero exit or a fresh file in crash-reports/).
/// </summary>
public sealed class GameProcess
{
    private readonly NovaPaths _paths;

    public event Action<GameExitInfo>? Exited;
    public event Action? Started;

    public Process? Process { get; private set; }
    public bool IsRunning => Process is { HasExited: false };

    public GameProcess(NovaPaths paths) => _paths = paths;

    public async Task<GameExitInfo> RunAsync(string javaExe, IReadOnlyList<string> arguments, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_paths.Logs);
        var gameLogPath = Path.Combine(_paths.Logs, $"game-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var launchTime = DateTime.Now;

        var psi = new ProcessStartInfo(javaExe)
        {
            WorkingDirectory = _paths.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        NovaLog.Info("Game", $"Launching: {javaExe} {LaunchArgumentBuilder.DescribeForLog(arguments)}");

        await using var logWriter = new StreamWriter(gameLogPath) { AutoFlush = true };
        Process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        Process.OutputDataReceived += (_, e) => { if (e.Data is not null) SafeWrite(logWriter, e.Data); };
        Process.ErrorDataReceived += (_, e) => { if (e.Data is not null) SafeWrite(logWriter, e.Data); };

        if (!Process.Start())
            throw new InvalidOperationException("The Java process could not be started.");
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
        Started?.Invoke();
        NovaLog.Info("Game", $"Game started (PID {Process.Id}). Output → {Path.GetFileName(gameLogPath)}");

        await Process.WaitForExitAsync(ct);
        var exitCode = Process.ExitCode;
        var crashReport = FindCrashReportSince(launchTime);
        var crashed = exitCode != 0 || crashReport is not null;
        var info = new GameExitInfo(exitCode, crashed, crashReport, gameLogPath);

        NovaLog.Info("Game", crashed
            ? $"Game exited abnormally (code {exitCode}){(crashReport is not null ? $", crash report: {Path.GetFileName(crashReport)}" : "")}."
            : "Game exited normally.");
        Exited?.Invoke(info);
        return info;
    }

    private static void SafeWrite(StreamWriter writer, string line)
    {
        try { lock (writer) writer.WriteLine(LogRedactor.Redact(line)); } catch { }
    }

    private string? FindCrashReportSince(DateTime time)
    {
        try
        {
            if (!Directory.Exists(_paths.CrashReports)) return null;
            return new DirectoryInfo(_paths.CrashReports).GetFiles("crash-*.txt")
                .Where(f => f.LastWriteTime >= time - TimeSpan.FromSeconds(5))
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault()?.FullName;
        }
        catch { return null; }
    }

    public void Kill()
    {
        try { if (IsRunning) Process!.Kill(entireProcessTree: true); } catch { }
    }
}
