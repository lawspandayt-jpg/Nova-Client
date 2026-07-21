using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using NovaClient.Authentication;
using NovaClient.Core.Logging;
using NovaClient.Core.Settings;
using NovaClient.Core.Util;
using NovaClient.GameClient;
using NovaClient.Launcher.Common;
using NovaClient.Launcher.Services;
using NovaClient.Minecraft;

namespace NovaClient.Launcher.ViewModels;

public sealed class HomeViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly AppServices _services;

    private PrepareResult? _prepared;
    private JavaInstall? _java;

    // ----- account -----
    public string Username => _services.Auth.Session?.Profile.Name ?? "—";
    public string Uuid => _services.Auth.Session?.Profile.Uuid ?? "—";
    public string OwnershipText => _services.Auth.Session?.OwnsMinecraft == true
        ? "Minecraft: Java Edition — owned ✓" : "Ownership not confirmed";
    public bool OwnershipOk => _services.Auth.Session?.OwnsMinecraft == true;

    private BitmapSource? _headImage;
    public BitmapSource? HeadImage { get => _headImage; set => Set(ref _headImage, value); }

    private BitmapSource? _skinImage;
    public BitmapSource? SkinImage { get => _skinImage; set => Set(ref _skinImage, value); }

    // ----- install/launch state -----
    private string _statusText = "Preparing…";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private string _speedText = "";
    public string SpeedText { get => _speedText; set => Set(ref _speedText, value); }

    private double _progress;
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    private bool _progressVisible;
    public bool ProgressVisible { get => _progressVisible; set => Set(ref _progressVisible, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

    private bool _gameRunning;
    public bool GameRunning { get => _gameRunning; set => Set(ref _gameRunning, value); }

    private string _javaStatus = "Detecting Java…";
    public string JavaStatus { get => _javaStatus; set => Set(ref _javaStatus, value); }

    private bool _javaOk;
    public bool JavaOk { get => _javaOk; set => Set(ref _javaOk, value); }

    private string _optiFineStatus = "Checking OptiFine…";
    public string OptiFineStatus { get => _optiFineStatus; set => Set(ref _optiFineStatus, value); }

    private bool _optiFineOk;
    public bool OptiFineOk { get => _optiFineOk; set => Set(ref _optiFineOk, value); }

    private string _updateText = "";
    public string UpdateText { get => _updateText; set => Set(ref _updateText, value); }

    private string _crashText = "";
    public string CrashText { get => _crashText; set => Set(ref _crashText, value); }

    // ----- version selection -----
    public ObservableCollection<string> Versions { get; } = new();

    // Changing version/Fabric only records the choice — downloads happen when Launch is clicked.
    public string SelectedVersion
    {
        get => _services.Settings.Current.SelectedVersion;
        set
        {
            if (_services.Settings.Current.SelectedVersion == value || string.IsNullOrEmpty(value)) return;
            _services.Settings.Current.SelectedVersion = value;
            _services.Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNovaVersion));
            OnPropertyChanged(nameof(VersionInfo));
            MarkPrepareStale();
        }
    }

    public bool UseFabric
    {
        get => _services.Settings.Current.UseFabric;
        set
        {
            if (_services.Settings.Current.UseFabric == value) return;
            _services.Settings.Current.UseFabric = value;
            _services.Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(VersionInfo));
            MarkPrepareStale();
        }
    }

    private void MarkPrepareStale()
    {
        StatusText = $"Ready — {VersionInfo} will download when you press Launch.";
        OnPropertyChanged(nameof(CanLaunch));
    }

    /// <summary>True when what's prepared on disk matches the current version/Fabric selection.</summary>
    private bool PreparedMatchesSelection =>
        _prepared is not null
        && _prepared.VersionId == SelectedVersion
        && (_prepared.FabricLoaderVersion is not null) == (UseFabric && !IsNovaVersion);

    /// <summary>The Nova in-game client (and OptiFine) load on 1.8.9; other versions run vanilla/Fabric.</summary>
    public bool IsNovaVersion => SelectedVersion == "1.8.9";

    public string VersionInfo => IsNovaVersion
        ? $"Minecraft 1.8.9 · {_services.Branding.ClientName} v{_services.Branding.GameClientVersion}"
        : $"Minecraft {SelectedVersion}{(UseFabric ? " · Fabric" : " · Vanilla")}";

    private bool _installReady;
    public bool InstallReady { get => _installReady; set => Set(ref _installReady, value); }

    // Launch is available as soon as the account checks out — any missing files/Java for the
    // current selection are downloaded as the first step of launching.
    public bool CanLaunch => !IsBusy && !GameRunning && OwnershipOk;

    // ----- commands -----
    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand RepairCommand { get; }
    public AsyncRelayCommand InstallJavaCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand OptiFineCommand { get; }
    public RelayCommand SignOutCommand { get; }
    public RelayCommand SwitchAccountCommand { get; }

    public HomeViewModel(MainViewModel main)
    {
        _main = main;
        _services = main.Services;
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, () => CanLaunch);
        RepairCommand = new AsyncRelayCommand(() => PrepareAsync(repair: true), () => !IsBusy && !GameRunning);
        InstallJavaCommand = new AsyncRelayCommand(InstallJavaAsync, () => !IsBusy && !JavaOk);
        SettingsCommand = new RelayCommand(main.ShowSettings);
        OptiFineCommand = new RelayCommand(main.ShowOptiFineSetup);
        SignOutCommand = new RelayCommand(() => main.SignOut(keepRememberedEmail: _services.Settings.Current.RememberEmail));
        SwitchAccountCommand = new RelayCommand(SwitchAccount);
    }

    public async Task LoadAsync()
    {
        _ = LoadSkinAsync();
        _ = CheckUpdatesAsync();
        _ = LoadVersionListAsync();
        await RefreshAsync();
    }

    /// <summary>Full pipeline for the selected version: install files, then ensure matching Java.</summary>
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        await PrepareAsync(repair: false);
        await EnsureJavaAsync();
        OnPropertyChanged(nameof(CanLaunch));
    }

    private async Task LoadVersionListAsync()
    {
        try
        {
            var releases = await _services.Launcher.Installer.GetReleaseVersionsAsync();
            // Curated like the big clients: latest two major lines + the classic PvP/modding anchors.
            var curated = new List<string>();
            void AddLatestOf(string prefix)
            {
                var match = releases.FirstOrDefault(id => id == prefix || id.StartsWith(prefix + "."));
                if (match is not null && !curated.Contains(match)) curated.Add(match);
            }
            AddLatestOf("1.21");
            AddLatestOf("1.20");
            foreach (var anchor in new[] { "1.19.4", "1.18.2", "1.17.1", "1.16.5", "1.12.2", "1.8.9" })
                if (releases.Contains(anchor) && !curated.Contains(anchor)) curated.Add(anchor);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Versions.Clear();
                foreach (var id in curated) Versions.Add(id);
                if (!Versions.Contains(SelectedVersion)) Versions.Insert(0, SelectedVersion);
                OnPropertyChanged(nameof(SelectedVersion));
            });
        }
        catch (Exception ex)
        {
            NovaLog.Warn("Versions", $"Could not load the version list: {ex.Message}");
            if (Versions.Count == 0) Versions.Add("1.8.9");
        }
    }

    private async Task LoadSkinAsync()
    {
        var profile = _services.Auth.Session?.Profile;
        if (profile is null) return;
        var file = await _services.Skins.GetSkinFileAsync(profile);
        if (file is null) return;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var skin = SkinImaging.LoadSkin(file);
            if (skin is null) return;
            HeadImage = SkinImaging.RenderHead(skin);
            SkinImage = SkinImaging.RenderBodyFront(skin);
        });
    }

    private int RequiredJavaMajor => _prepared?.RequiredJavaMajor ?? 8;

    /// <summary>Finds a JVM matching the selected version's requirement; auto-installs Temurin if none.</summary>
    private async Task EnsureJavaAsync()
    {
        var required = RequiredJavaMajor;
        var settings = _services.Settings.Current;
        if (!string.IsNullOrEmpty(settings.JavaPath))
        {
            _java = await JavaLocator.ProbeAsync(settings.JavaPath);
            if (_java is not null && _java.Satisfies(required))
            {
                JavaStatus = $"Java: {_java.Description} (manual)";
                JavaOk = true;
                return;
            }
        }

        var all = await JavaLocator.DetectAllAsync(_services.Paths.JavaRuntime);
        _java = all.FirstOrDefault(j => j.Satisfies(required));
        if (_java is not null)
        {
            JavaStatus = $"Java: {_java.Description}";
            JavaOk = true;
            return;
        }

        JavaOk = false;
        JavaStatus = $"Minecraft {SelectedVersion} needs 64-bit Java {required} — installing it automatically…";
        await InstallJavaAsync();
    }

    private async Task InstallJavaAsync()
    {
        var required = RequiredJavaMajor;
        IsBusy = true;
        ProgressVisible = true;
        StatusText = $"Downloading Eclipse Temurin {required} (official Adoptium build)…";
        try
        {
            var provider = new AdoptiumJavaProvider(_services.Paths.JavaRuntime, _services.Paths.Cache, required);
            var progress = new Progress<DownloadProgress>(p =>
            {
                Progress = p.BytesTotal > 0 ? (double)p.BytesDone / p.BytesTotal : 0;
                SpeedText = FormatSpeed(p.BytesPerSecond);
            });
            _java = await provider.InstallAsync(progress);
            JavaStatus = $"Java: {_java.Description}";
            JavaOk = _java.Satisfies(required);
            StatusText = "Ready to launch.";
        }
        catch (Exception ex)
        {
            NovaLog.Error("Java", "Java install failed", ex);
            StatusText = "Java installation failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            ProgressVisible = false;
            SpeedText = "";
            OnPropertyChanged(nameof(CanLaunch));
        }
    }

    private async Task PrepareAsync(bool repair)
    {
        IsBusy = true;
        InstallReady = false;
        CrashText = "";
        ProgressVisible = true;
        StatusText = repair ? "Repairing installation…" : "Verifying game files…";
        try
        {
            var progress = new Progress<InstallPhase>(phase =>
            {
                StatusText = phase.Name;
                if (phase.Download is { } d)
                {
                    Progress = d.BytesTotal > 0 ? (double)d.BytesDone / d.BytesTotal : 0;
                    SpeedText = d.BytesTotal > 0
                        ? $"{FormatBytes(d.BytesDone)} / {FormatBytes(d.BytesTotal)} · {FormatSpeed(d.BytesPerSecond)} · {d.FilesDone}/{d.FilesTotal} files"
                        : "";
                }
            });

            var versionId = SelectedVersion;
            var useFabric = UseFabric && !IsNovaVersion;
            _prepared = await Task.Run(() => _services.Launcher.PrepareAsync(versionId, useFabric, progress));

            if (_prepared.IsNovaVersion && !_prepared.NovaClientJarPresent)
            {
                StatusText = "This build is missing client/nova-client.jar — run client-java\\build.ps1 and rebuild.";
                InstallReady = false;
            }
            else
            {
                InstallReady = true;
                StatusText = "Ready to launch.";
            }

            if (_prepared.IsNovaVersion)
            {
                var optifine = _prepared.OptiFine;
                OptiFineOk = optifine is not null;
                OptiFineStatus = optifine is not null
                    ? $"OptiFine {optifine.Edition} — enabled ✓"
                    : "OptiFine not installed (optional — adds zoom + better FPS).";
            }
            else
            {
                OptiFineOk = true;
                OptiFineStatus = _prepared.FabricLoaderVersion is not null
                    ? $"Fabric loader {_prepared.FabricLoaderVersion} — enabled ✓ (drop mods in the mods folder)"
                    : $"Vanilla {SelectedVersion} — the Nova client & OptiFine load on 1.8.9 only.";
            }
        }
        catch (Exception ex)
        {
            NovaLog.Error("Install", "Installation failed", ex);
            StatusText = "Installation failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            ProgressVisible = false;
            SpeedText = "";
            OnPropertyChanged(nameof(CanLaunch));
        }
    }

    private async Task LaunchAsync()
    {
        // Download-on-launch: bring files + Java in line with the current selection first.
        if (!PreparedMatchesSelection || !InstallReady)
        {
            await PrepareAsync(repair: false);
            if (!InstallReady) return;
        }
        if (_java is null || !JavaOk || !_java.Satisfies(RequiredJavaMajor))
        {
            await EnsureJavaAsync();
            if (!JavaOk) return;
        }
        if (_prepared is null || _java is null) return;
        CrashText = "";
        IsBusy = true;
        try
        {
            StatusText = "Refreshing session…";
            var session = await _services.Auth.EnsureFreshAsync();

            StatusText = "Starting Minecraft…";
            var identity = new LaunchIdentity(session.Profile.Name, session.Profile.Uuid, session.MinecraftAccessToken);
            GameRunning = true;

            var afterLaunch = _services.Settings.Current.AfterLaunch;
            var mainWindow = Application.Current.MainWindow;
            switch (afterLaunch)
            {
                case AfterLaunchBehavior.Minimize:
                    mainWindow.WindowState = WindowState.Minimized;
                    break;
                case AfterLaunchBehavior.Close:
                    // Keep the process alive until the game exits so logs/crash detection work,
                    // but hide the window entirely.
                    mainWindow.Hide();
                    break;
            }

            StatusText = "Minecraft is running…";
            var exit = await _services.Launcher.LaunchAsync(_prepared, identity, _services.Settings.Current, _java.ExecutablePath);

            GameRunning = false;
            if (afterLaunch == AfterLaunchBehavior.Close)
            {
                Application.Current.Shutdown();
                return;
            }
            if (afterLaunch == AfterLaunchBehavior.Minimize)
                mainWindow.WindowState = WindowState.Normal;

            if (exit.Crashed)
            {
                CrashText = $"Minecraft exited abnormally (code {exit.ExitCode})."
                            + (exit.CrashReportPath is not null ? $"\nCrash report: {exit.CrashReportPath}" : "")
                            + $"\nGame log: {exit.GameLogPath}";
                StatusText = "Game crashed — details below.";
            }
            else
            {
                StatusText = "Ready to launch.";
            }
        }
        catch (AuthException ex)
        {
            StatusText = ex.UserMessage;
            GameRunning = false;
        }
        catch (Exception ex)
        {
            NovaLog.Error("Launch", "Launch failed", ex);
            StatusText = "Launch failed: " + ex.Message;
            GameRunning = false;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanLaunch));
        }
    }

    private async Task CheckUpdatesAsync()
    {
        if (_services.Settings.Current.DisableDevUpdateChecks) { UpdateText = "Update checks disabled."; return; }
        try
        {
            var result = await _services.Updater.CheckAsync(_services.Branding.LauncherVersion);
            UpdateText = result.UpdateAvailable
                ? $"Update {result.LatestVersion} available — {result.Notes}"
                : $"Launcher is up to date (v{result.CurrentVersion}).";
        }
        catch (Exception ex)
        {
            NovaLog.Debug("Updater", $"Update check skipped: {ex.Message}");
            UpdateText = "Update check unavailable.";
        }
    }

    private void SwitchAccount()
    {
        // Keeps the current account saved (with its tokens) so it appears in the login screen's
        // quick-switch list; only the active session is cleared.
        _services.Auth.Deactivate();
        _main.ShowLogin();
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1 << 30 => $"{bytes / (double)(1 << 30):0.0} GB",
        >= 1 << 20 => $"{bytes / (double)(1 << 20):0.0} MB",
        >= 1 << 10 => $"{bytes / (double)(1 << 10):0.0} KB",
        _ => $"{bytes} B"
    };

    private static string FormatSpeed(double bytesPerSecond) => FormatBytes((long)bytesPerSecond) + "/s";
}
