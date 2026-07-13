using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using NovaClient.Core.Logging;
using NovaClient.Core.Settings;
using NovaClient.Launcher.Common;
using NovaClient.Launcher.Services;
using NovaClient.Minecraft;

namespace NovaClient.Launcher.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly AppServices _services;
    private readonly LauncherSettings _settings;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _services = main.Services;
        _settings = _services.Settings.Current;

        BackCommand = new RelayCommand(GoBack);
        BrowseJavaCommand = new RelayCommand(BrowseJava);
        AutoDetectJavaCommand = new AsyncRelayCommand(AutoDetectJavaAsync);
        BrowseGameDirCommand = new RelayCommand(BrowseGameDir);
        OpenScreenshotsCommand = new RelayCommand(() => OpenFolder(_services.Paths.Screenshots));
        OpenResourcePacksCommand = new RelayCommand(() => OpenFolder(_services.Paths.ResourcePacks));
        OpenGameDirCommand = new RelayCommand(() => OpenFolder(_services.Paths.Root));
        OpenLogsCommand = new RelayCommand(() => OpenFolder(_services.Paths.Logs));
        RepairCommand = new RelayCommand(() => { GoBack(); });
        ClearCacheCommand = new RelayCommand(ClearCache);
        ResetLauncherSettingsCommand = new RelayCommand(ResetLauncherSettings);
        ResetClientSettingsCommand = new RelayCommand(ResetClientSettings);
        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync);

        _ = LoadJavaInfoAsync();
    }

    // ----- RAM -----
    public int RamMb
    {
        get => _settings.RamMb;
        set { _settings.RamMb = RamValidator.Clamp(value); OnPropertyChanged(); OnPropertyChanged(nameof(RamText)); Save(); }
    }
    public int RamMin => RamValidator.MinimumMb;
    public int RamMax => RamValidator.MaximumMb;
    public string RamText => $"{RamMb} MB (recommended: {RamValidator.Recommended()} MB · system: {RamValidator.TotalPhysicalMb} MB)";

    // ----- window -----
    public int WindowWidth
    {
        get => _settings.WindowWidth;
        set { _settings.WindowWidth = Math.Clamp(value, 640, 7680); OnPropertyChanged(); Save(); }
    }
    public int WindowHeight
    {
        get => _settings.WindowHeight;
        set { _settings.WindowHeight = Math.Clamp(value, 480, 4320); OnPropertyChanged(); Save(); }
    }
    public bool Fullscreen
    {
        get => _settings.Fullscreen;
        set { _settings.Fullscreen = value; OnPropertyChanged(); Save(); }
    }

    // ----- Java -----
    private string _javaInfo = "";
    public string JavaInfo { get => _javaInfo; set => Set(ref _javaInfo, value); }

    public string JavaPath
    {
        get => _settings.JavaPath ?? "";
        set { _settings.JavaPath = string.IsNullOrWhiteSpace(value) ? null : value; OnPropertyChanged(); Save(); _ = LoadJavaInfoAsync(); }
    }

    // ----- game dir / args -----
    public string GameDirectory
    {
        get => _settings.GameDirectory ?? _services.Paths.Root;
        set { _settings.GameDirectory = string.IsNullOrWhiteSpace(value) ? null : value; OnPropertyChanged(); Save(); }
    }
    public string ExtraJvmArgs
    {
        get => _settings.ExtraJvmArgs;
        set { _settings.ExtraJvmArgs = value; OnPropertyChanged(); Save(); }
    }

    // ----- behavior -----
    public bool KeepOpen
    {
        get => _settings.AfterLaunch == AfterLaunchBehavior.KeepOpen;
        set { if (value) SetAfterLaunch(AfterLaunchBehavior.KeepOpen); }
    }
    public bool MinimizeAfterLaunch
    {
        get => _settings.AfterLaunch == AfterLaunchBehavior.Minimize;
        set { if (value) SetAfterLaunch(AfterLaunchBehavior.Minimize); }
    }
    public bool CloseAfterLaunch
    {
        get => _settings.AfterLaunch == AfterLaunchBehavior.Close;
        set { if (value) SetAfterLaunch(AfterLaunchBehavior.Close); }
    }
    private void SetAfterLaunch(AfterLaunchBehavior behavior)
    {
        _settings.AfterLaunch = behavior;
        OnPropertyChanged(nameof(KeepOpen));
        OnPropertyChanged(nameof(MinimizeAfterLaunch));
        OnPropertyChanged(nameof(CloseAfterLaunch));
        Save();
    }

    public bool RememberEmail
    {
        get => _settings.RememberEmail;
        set
        {
            _settings.RememberEmail = value;
            if (!value) _settings.RememberedEmail = string.Empty;
            OnPropertyChanged();
            Save();
        }
    }

    public bool DisableDevUpdateChecks
    {
        get => _settings.DisableDevUpdateChecks;
        set { _settings.DisableDevUpdateChecks = value; OnPropertyChanged(); Save(); }
    }

    private string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; set => Set(ref _updateStatus, value); }

    // ----- commands -----
    public RelayCommand BackCommand { get; }
    public RelayCommand BrowseJavaCommand { get; }
    public AsyncRelayCommand AutoDetectJavaCommand { get; }
    public RelayCommand BrowseGameDirCommand { get; }
    public RelayCommand OpenScreenshotsCommand { get; }
    public RelayCommand OpenResourcePacksCommand { get; }
    public RelayCommand OpenGameDirCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand RepairCommand { get; }
    public RelayCommand ClearCacheCommand { get; }
    public RelayCommand ResetLauncherSettingsCommand { get; }
    public RelayCommand ResetClientSettingsCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }

    private void Save() => _services.Settings.Save();

    private void GoBack()
    {
        if (_services.Auth.Session is not null) _main.ShowHome();
        else _main.ShowLogin();
    }

    private async Task LoadJavaInfoAsync()
    {
        if (!string.IsNullOrEmpty(_settings.JavaPath))
        {
            var probe = await JavaLocator.ProbeAsync(_settings.JavaPath);
            JavaInfo = probe?.Description ?? "Selected file is not a working Java executable.";
            return;
        }
        var all = await JavaLocator.DetectAllAsync(_services.Paths.JavaRuntime);
        var best = all.FirstOrDefault(j => j.IsCompatible) ?? all.FirstOrDefault();
        JavaInfo = best is null ? "No Java detected — the home screen offers a one-click install." : $"Auto-detected: {best.Description}";
    }

    private void BrowseJava()
    {
        var dialog = new OpenFileDialog { Filter = "Java executable|java.exe", Title = "Select java.exe (64-bit Java 8)" };
        if (dialog.ShowDialog() == true) JavaPath = dialog.FileName;
    }

    private async Task AutoDetectJavaAsync()
    {
        JavaPath = "";
        await LoadJavaInfoAsync();
    }

    private void BrowseGameDir()
    {
        var dialog = new OpenFolderDialog { Title = "Select the Nova Client data folder" };
        if (dialog.ShowDialog() == true)
        {
            GameDirectory = dialog.FolderName;
            MessageBox.Show("The new data folder will be used after the launcher restarts.",
                "Nova Client", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void ClearCache()
    {
        try
        {
            _services.Launcher.Installer.ClearDownloadCache();
            MessageBox.Show("Download cache cleared.", "Nova Client", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            NovaLog.Error("Settings", "Cache clear failed", ex);
            MessageBox.Show("Could not clear the cache: " + ex.Message, "Nova Client", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetLauncherSettings()
    {
        if (MessageBox.Show("Reset all launcher settings to defaults?", "Nova Client",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _services.Settings.Reset();
        _main.ShowSettings(); // rebuild the page with fresh values
    }

    private void ResetClientSettings()
    {
        // The in-game client persists its settings in config/client-settings.json.
        var file = Path.Combine(_services.Paths.Config, "client-settings.json");
        if (MessageBox.Show("Reset in-game client settings (GUI layout, theme, keybinds)?", "Nova Client",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { if (File.Exists(file)) File.Delete(file); } catch (Exception ex) { NovaLog.Warn("Settings", ex.Message); }
        MessageBox.Show("Client settings will be recreated with defaults on the next game start.",
            "Nova Client", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task CheckUpdatesAsync()
    {
        try
        {
            var result = await _services.Updater.CheckAsync(_services.Branding.LauncherVersion);
            UpdateStatus = result.UpdateAvailable
                ? $"Update available: v{result.LatestVersion} — {result.Notes}"
                : $"You are on the latest version (v{result.CurrentVersion}).";
        }
        catch (Exception ex)
        {
            UpdateStatus = "Update check failed: " + ex.Message;
        }
    }
}
