using System.Collections.Concurrent;

namespace NovaClient.Core.Logging;

public enum LogLevel { Debug, Info, Warn, Error }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Component, string Message);

/// <summary>
/// Structured launcher log. Every message is passed through <see cref="LogRedactor"/> before it is
/// stored, raised to the UI, or written to disk. Writes happen on a background worker so logging
/// never blocks the UI thread.
/// </summary>
public static class NovaLog
{
    private static readonly BlockingCollection<LogEntry> Queue = new();
    private static StreamWriter? _writer;
    private static Task? _worker;

    public static event Action<LogEntry>? EntryLogged;

    public static string? CurrentLogFile { get; private set; }

    public static void Init(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        CurrentLogFile = Path.Combine(logDirectory, $"launcher-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _writer = new StreamWriter(new FileStream(CurrentLogFile, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
        _worker = Task.Run(() =>
        {
            foreach (var entry in Queue.GetConsumingEnumerable())
            {
                try
                {
                    _writer?.WriteLine($"[{entry.Time:HH:mm:ss.fff}] [{entry.Level,-5}] [{entry.Component}] {entry.Message}");
                }
                catch { /* logging must never crash the launcher */ }
            }
        });
        CleanupOldLogs(logDirectory);
    }

    private static void CleanupOldLogs(string logDirectory)
    {
        try
        {
            var old = new DirectoryInfo(logDirectory).GetFiles("launcher-*.log")
                .OrderByDescending(f => f.CreationTimeUtc).Skip(20);
            foreach (var f in old) f.Delete();
        }
        catch { }
    }

    public static void Debug(string component, string message) => Write(LogLevel.Debug, component, message);
    public static void Info(string component, string message) => Write(LogLevel.Info, component, message);
    public static void Warn(string component, string message) => Write(LogLevel.Warn, component, message);
    public static void Error(string component, string message) => Write(LogLevel.Error, component, message);

    public static void Error(string component, string message, Exception ex) =>
        Write(LogLevel.Error, component, $"{message}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    private static void Write(LogLevel level, string component, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, component, LogRedactor.Redact(message));
        if (!Queue.IsAddingCompleted) Queue.Add(entry);
        EntryLogged?.Invoke(entry);
    }

    public static void Shutdown()
    {
        Queue.CompleteAdding();
        try { _worker?.Wait(2000); } catch { }
        _writer?.Dispose();
        _writer = null;
    }
}
