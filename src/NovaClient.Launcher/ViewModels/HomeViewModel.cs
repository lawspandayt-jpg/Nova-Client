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
    public string StatusText
    {
        get => _statusText;
        set { if (Set(ref _statusText, value)) OnPropertyChanged(nameof(LaunchButtonText)); }
    }

    /// <summary>The launch button doubles as the stage indicator while busy.</summary>
    public string LaunchButtonText => IsBusy || GameRunning ? StatusText : "LAUNCH GAME";

    public string RamText => $"{Core.Settings.RamValidator.Clamp(_services.Settings.Current.RamMb)} MB RAM";

    private bool _accountMenuOpen;
    public bool AccountMenuOpen { get => _accountMenuOpen; set => Set(ref _accountMenuOpen, value); }

    public sealed record NewsItem(string Title, string Date, string Summary);

    public ObservableCollection<NewsItem> News { get; } = new();

    /// <summary>Real news pulled from the project's GitHub releases; hidden when unavailable.</summary>
    private async Task LoadNewsAsync()
    {
        try
        {
            var json = await Core.Http.HttpProvider.Client.GetStringAsync(
                "https://api.github.com/repos/lawspandayt-jpg/Nova-Client/releases?per_page=3");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var items = new List<NewsItem>();
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var title = release.GetProperty("name").GetString() ?? release.GetProperty("tag_name").GetString() ?? "Release";
                var date = release.TryGetProperty("published_at", out var p) && p.GetString() is { } iso
                    ? DateTimeOffset.Parse(iso).ToString("MMM d, yyyy") : "";
                var body = release.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                var summary = body.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
                items.Add(new NewsItem(title, date, summary));
            }
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                News.Clear();
                foreach (var item in items) News.Add(item);
            });
        }
        catch (Exception ex)
        {
            Core.Logging.NovaLog.Debug("News", $"News unavailable: {ex.Message}");
        }
    }

    // ----- friends -----
    public sealed class FriendItem : ViewModelBase
    {
        public required string Uuid { get; init; }
        public required string Name { get; init; }
        public required RelayCommand RemoveCommand { get; init; }

        private System.Windows.Media.Imaging.BitmapSource? _head;
        public System.Windows.Media.Imaging.BitmapSource? Head { get => _head; set => Set(ref _head, value); }

        private bool _online;
        public bool Online { get => _online; set => Set(ref _online, value); }

        private string _status = "Added";
        public string Status { get => _status; set => Set(ref _status, value); }
    }

    public ObservableCollection<FriendItem> Friends { get; } = new();

    private string _addFriendName = "";
    public string AddFriendName { get => _addFriendName; set { if (Set(ref _addFriendName, value)) FriendError = ""; } }

    private string _friendError = "";
    public string FriendError { get => _friendError; set => Set(ref _friendError, value); }

    private bool _presenceEnabled;
    public bool PresenceEnabled { get => _presenceEnabled; set => Set(ref _presenceEnabled, value); }

    public int OnlineFriendCount => Friends.Count(f => f.Online);
    public string FriendsHeader => PresenceEnabled ? $"Friends ({OnlineFriendCount} online)" : $"Friends ({Friends.Count})";

    public AsyncRelayCommand AddFriendCommand { get; }

    private async Task LoadFriendsAsync()
    {
        PresenceEnabled = _services.Presence.Enabled;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Friends.Clear();
            foreach (var friend in _services.Friends.Friends) Friends.Add(BuildFriendItem(friend));
        });
        _ = LoadFriendHeadsAsync();
        _ = RefreshPresenceAsync();
        OnPropertyChanged(nameof(FriendsHeader));
    }

    private FriendItem BuildFriendItem(Services.Friend friend)
    {
        var uuid = friend.Uuid;
        return new FriendItem
        {
            Uuid = uuid,
            Name = friend.Name,
            RemoveCommand = new RelayCommand(() =>
            {
                _services.Friends.Remove(uuid);
                var existing = Friends.FirstOrDefault(f => f.Uuid == uuid);
                if (existing is not null) Friends.Remove(existing);
                OnPropertyChanged(nameof(FriendsHeader));
            })
        };
    }

    private async Task LoadFriendHeadsAsync()
    {
        foreach (var friend in _services.Friends.Friends.ToList())
        {
            var item = Friends.FirstOrDefault(f => f.Uuid == friend.Uuid);
            if (item is null || string.IsNullOrEmpty(friend.SkinUrl)) continue;
            try
            {
                var bytes = await Core.Http.HttpProvider.Client.GetByteArrayAsync(friend.SkinUrl);
                var cache = Path.Combine(_services.Paths.Cache, "skins", friend.Uuid + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                await File.WriteAllBytesAsync(cache, bytes);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var skin = SkinImaging.LoadSkin(cache);
                    if (skin is not null) item.Head = SkinImaging.RenderHead(skin);
                });
            }
            catch { }
        }
    }

    private async Task RefreshPresenceAsync()
    {
        if (!_services.Presence.Enabled) return;
        var session = _services.Auth.Session;
        if (session is not null)
            _ = _services.Presence.HeartbeatAsync(session.Profile.Uuid, session.Profile.Name);
        var online = await _services.Presence.GetOnlineAsync(Friends.Select(f => f.Uuid));
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var friend in Friends)
            {
                friend.Online = online.Contains(friend.Uuid);
                friend.Status = friend.Online ? "Online" : "Offline";
            }
            OnPropertyChanged(nameof(OnlineFriendCount));
            OnPropertyChanged(nameof(FriendsHeader));
        });
    }

    private async Task AddFriendAsync()
    {
        FriendError = "";
        var name = AddFriendName.Trim();
        if (name.Length == 0) return;
        try
        {
            var friend = await _services.Friends.AddByNameAsync(name);
            var item = BuildFriendItem(friend);
            Friends.Add(item);
            AddFriendName = "";
            OnPropertyChanged(nameof(FriendsHeader));
            _ = LoadFriendHeadsAsync();
        }
        catch (Exception ex)
        {
            FriendError = ex.Message;
        }
    }

    public ObservableCollection<string> RecentActivity { get; } = new();
    private readonly Action<Core.Logging.LogEntry> _activityHandler;

    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand CopyErrorCommand { get; }
    public RelayCommand ToggleAccountMenuCommand { get; }

    private string _speedText = "";
    public string SpeedText { get => _speedText; set => Set(ref _speedText, value); }

    private double _progress;
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    private bool _progressVisible;
    public bool ProgressVisible { get => _progressVisible; set => Set(ref _progressVisible, value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { if (Set(ref _isBusy, value)) OnPropertyChanged(nameof(LaunchButtonText)); }
    }

    private bool _gameRunning;
    public bool GameRunning
    {
        get => _gameRunning;
        set { if (Set(ref _gameRunning, value)) OnPropertyChanged(nameof(LaunchButtonText)); }
    }

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
    public RelayCommand ChangeVersionCommand { get; }
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
        ChangeVersionCommand = new RelayCommand(main.ShowVersions);
        OpenLogsCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(_services.Paths.Logs);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{_services.Paths.Logs}\"") { UseShellExecute = true });
        });
        CopyErrorCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(CrashText)) Clipboard.SetText(CrashText);
        });
        ToggleAccountMenuCommand = new RelayCommand(() => AccountMenuOpen = !AccountMenuOpen);
        AddFriendCommand = new AsyncRelayCommand(AddFriendAsync);

        // Recent activity: mirror meaningful launcher events (redacted upstream by NovaLog).
        var interesting = new HashSet<string> { "Auth", "Game", "Launcher", "Install", "OptiFine", "Java", "Updater", "Fabric", "App" };
        _activityHandler = entry =>
        {
            if (entry.Level != Core.Logging.LogLevel.Info || !interesting.Contains(entry.Component)) return;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                RecentActivity.Insert(0, $"{entry.Time:HH:mm}  {entry.Message}");
                while (RecentActivity.Count > 7) RecentActivity.RemoveAt(RecentActivity.Count - 1);
            });
        };
        Core.Logging.NovaLog.EntryLogged += _activityHandler;
        OptiFineCommand = new RelayCommand(main.ShowOptiFineSetup);
        SignOutCommand = new RelayCommand(() => main.SignOut(keepRememberedEmail: _services.Settings.Current.RememberEmail));
        SwitchAccountCommand = new RelayCommand(SwitchAccount);
    }

    public async Task LoadAsync(bool repair = false)
    {
        _ = LoadSkinAsync();
        _ = CheckUpdatesAsync();
        _ = LoadNewsAsync();
        _ = LoadFriendsAsync();
        await LoadVersionListAsync();
        if (repair)
        {
            // Arriving from Settings → Repair Installation: run a full verify immediately.
            await PrepareAsync(repair: true);
            await EnsureJavaAsync();
            OnPropertyChanged(nameof(CanLaunch));
            return;
        }

        // Download-on-launch: nothing heavy happens here. Show cheap local status only.
        var optifine = _services.Launcher.OptiFine.DetectInstalled();
        OptiFineOk = !IsNovaVersion || optifine is not null;
        OptiFineStatus = IsNovaVersion
            ? optifine is not null
                ? $"OptiFine {optifine.Edition} — enabled ✓"
                : "OptiFine not installed (optional — adds zoom + better FPS)."
            : UseFabric ? "Fabric — enabled ✓" : $"Vanilla {SelectedVersion}";
        JavaOk = true;
        JavaStatus = "Java: checked automatically at launch.";
        InstallReady = false;
        StatusText = $"Ready — {VersionInfo} verifies and downloads when you press Launch.";
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

    /// <summary>Called when navigating away so log-event subscriptions don't accumulate.</summary>
    public void Detach() => Core.Logging.NovaLog.EntryLogged -= _activityHandler;

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
