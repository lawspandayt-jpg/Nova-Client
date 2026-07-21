using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using NovaClient.Core.Logging;
using NovaClient.Launcher.Common;

namespace NovaClient.Launcher.ViewModels;

/// <summary>The Logs sidebar page: live activity feed + log-folder tools.</summary>
public sealed class LogsViewModel : ViewModelBase
{
    public ObservableCollection<string> Activity { get; } = new();
    public RelayCommand OpenLogsFolderCommand { get; }
    public RelayCommand OpenCrashReportsCommand { get; }

    private readonly Action<LogEntry> _handler;

    public LogsViewModel(MainViewModel main)
    {
        OpenLogsFolderCommand = new RelayCommand(() => OpenFolder(main.Services.Paths.Logs));
        OpenCrashReportsCommand = new RelayCommand(() => OpenFolder(main.Services.Paths.CrashReports));

        // Show the tail of the current session's launcher log (already redacted).
        try
        {
            var file = NovaLog.CurrentLogFile;
            if (file is not null && File.Exists(file))
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var lines = new List<string>();
                while (reader.ReadLine() is { } line) lines.Add(line);
                foreach (var line in lines.TakeLast(40)) Activity.Add(line);
            }
        }
        catch { }

        _handler = entry => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Activity.Add($"[{entry.Time:HH:mm:ss}] [{entry.Level}] [{entry.Component}] {entry.Message}");
            while (Activity.Count > 200) Activity.RemoveAt(0);
        });
        NovaLog.EntryLogged += _handler;
    }

    public void Detach() => NovaLog.EntryLogged -= _handler;

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
