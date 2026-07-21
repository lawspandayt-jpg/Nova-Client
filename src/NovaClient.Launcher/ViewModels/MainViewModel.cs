using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using NovaClient.Core.Logging;
using NovaClient.Launcher.Common;
using NovaClient.Launcher.Services;

namespace NovaClient.Launcher.ViewModels;

public sealed class HeaderAccount
{
    public required string Uuid { get; init; }
    public required string Name { get; init; }
    public BitmapSource? Head { get; set; }
    public required bool IsActive { get; init; }
    public required RelayCommand SelectCommand { get; init; }
}

public sealed class MainViewModel : ViewModelBase
{
    public AppServices Services { get; }

    private ViewModelBase? _current;
    public ViewModelBase? Current
    {
        get => _current;
        private set
        {
            (_current as HomeViewModel)?.Detach();
            (_current as LogsViewModel)?.Detach();
            Set(ref _current, value);
        }
    }

    private string _activePage = "Home";
    public string ActivePage { get => _activePage; private set => Set(ref _activePage, value); }

    public string SelectedVersionText => Services.Settings.Current.SelectedVersion;

    public RelayCommand NavModsCommand { get; private set; } = null!;
    public RelayCommand NavServersCommand { get; private set; } = null!;

    public string LauncherTitle => Services.Branding.LauncherTitle;
    public string VersionText => $"v{Services.Branding.LauncherVersion}";

    // ----- header account menu -----
    public bool IsSignedIn => Services.Auth.Session is not null;
    public string CurrentUsername => Services.Auth.Session?.Profile.Name ?? "";

    private BitmapSource? _currentHead;
    public BitmapSource? CurrentHead { get => _currentHead; private set => Set(ref _currentHead, value); }

    private bool _accountMenuOpen;
    public bool AccountMenuOpen { get => _accountMenuOpen; set => Set(ref _accountMenuOpen, value); }

    public ObservableCollection<HeaderAccount> SavedAccounts { get; } = new();

    public RelayCommand ToggleAccountMenuCommand { get; }
    public RelayCommand AddAccountCommand { get; }
    public RelayCommand ChangeSkinCommand { get; }
    public RelayCommand OpenScreenshotsCommand { get; }
    public RelayCommand AccountSettingsCommand { get; }
    public RelayCommand HeaderSignOutCommand { get; }

    // Sidebar navigation
    public RelayCommand NavPlayCommand { get; }
    public RelayCommand NavVersionsCommand { get; }
    public RelayCommand NavSettingsCommand { get; }
    public RelayCommand NavOptiFineCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand OpenWebsiteCommand { get; }

    public MainViewModel(AppServices services)
    {
        Services = services;
        NavPlayCommand = new RelayCommand(() =>
        {
            if (Services.Auth.Session is not null) ShowHome();
            else ShowLogin();
        });
        NavVersionsCommand = new RelayCommand(() =>
        {
            if (Services.Auth.Session is not null) ShowVersions();
        });
        NavSettingsCommand = new RelayCommand(ShowSettings);
        NavLogsCommand = new RelayCommand(ShowLogs);
        NavModsCommand = new RelayCommand(() => ShowComingSoon("Mods",
            "Browse and manage Fabric mods for modern versions right from the launcher."));
        NavServersCommand = new RelayCommand(() => ShowComingSoon("Servers",
            "Save favorite servers and jump straight into them."));
        NavOptiFineCommand = new RelayCommand(() =>
        {
            if (Services.Auth.Session is not null) ShowOptiFineSetup();
        });
        OpenFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(Services.Paths.Root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Services.Paths.Root}\"") { UseShellExecute = true });
        });
        OpenWebsiteCommand = new RelayCommand(() =>
        {
            try { Process.Start(new ProcessStartInfo(Services.Branding.WebsiteUrl) { UseShellExecute = true }); }
            catch (Exception ex) { NovaLog.Warn("App", $"Could not open website: {ex.Message}"); }
        });

        ToggleAccountMenuCommand = new RelayCommand(() => AccountMenuOpen = !AccountMenuOpen);
        AddAccountCommand = new RelayCommand(() =>
        {
            AccountMenuOpen = false;
            // Keeps existing accounts saved; the login screen offers them plus fresh email entry.
            Services.Auth.Deactivate();
            ShowLogin();
        });
        ChangeSkinCommand = new RelayCommand(() =>
        {
            AccountMenuOpen = false;
            // Skins/capes are managed on Minecraft's official site (we never handle credentials here).
            try { Process.Start(new ProcessStartInfo("https://www.minecraft.net/msaprofile/mygames/editskin") { UseShellExecute = true }); }
            catch (Exception ex) { NovaLog.Warn("App", $"Could not open skin page: {ex.Message}"); }
        });
        OpenScreenshotsCommand = new RelayCommand(() =>
        {
            AccountMenuOpen = false;
            Directory.CreateDirectory(Services.Paths.Screenshots);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Services.Paths.Screenshots}\"") { UseShellExecute = true });
        });
        AccountSettingsCommand = new RelayCommand(() => { AccountMenuOpen = false; ShowSettings(); });
        HeaderSignOutCommand = new RelayCommand(() =>
        {
            AccountMenuOpen = false;
            SignOut(keepRememberedEmail: Services.Settings.Current.RememberEmail);
        });

        ShowLogin(restoreSession: true);
    }

    /// <summary>Refreshes the header account chip + switch menu; called whenever the session changes.</summary>
    public void RefreshAccountMenu()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(CurrentUsername));
        SavedAccounts.Clear();
        var activeUuid = Services.Auth.Session?.Profile.Uuid;
        foreach (var account in Services.Auth.GetSavedAccounts())
        {
            var uuid = account.Uuid;
            SavedAccounts.Add(new HeaderAccount
            {
                Uuid = uuid,
                Name = account.Name,
                IsActive = uuid == activeUuid,
                SelectCommand = new RelayCommand(async () =>
                {
                    AccountMenuOpen = false;
                    if (uuid == Services.Auth.Session?.Profile.Uuid) return;
                    var session = await Services.Auth.ActivateAsync(uuid);
                    if (session is not null) ShowHome();
                    else ShowLogin();
                })
            });
        }
        _ = LoadCurrentHeadAsync();
    }

    private async Task LoadCurrentHeadAsync()
    {
        var profile = Services.Auth.Session?.Profile;
        if (profile is null) { CurrentHead = null; return; }
        try
        {
            var file = await Services.Skins.GetSkinFileAsync(profile);
            if (file is null) return;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var skin = SkinImaging.LoadSkin(file);
                if (skin is not null) CurrentHead = SkinImaging.RenderHead(skin);
            });
        }
        catch (Exception ex) { NovaLog.Debug("App", $"Header head load failed: {ex.Message}"); }
    }

    public void ShowLogin(bool restoreSession = false)
    {
        var vm = new LoginViewModel(this);
        Current = vm;
        ActivePage = "Home";
        RefreshAccountMenu();
        if (restoreSession) _ = vm.TryRestoreSessionAsync();
    }

    public void ShowHome(bool repair = false)
    {
        var vm = new HomeViewModel(this);
        Current = vm;
        ActivePage = "Home";
        OnPropertyChanged(nameof(SelectedVersionText));
        RefreshAccountMenu();
        _ = vm.LoadAsync(repair);
    }

    public void ShowSettings()
    {
        Current = new SettingsViewModel(this);
        ActivePage = "Settings";
    }

    public void ShowVersions()
    {
        Current = new VersionsViewModel(this);
        ActivePage = "Versions";
    }

    public void ShowOptiFineSetup()
    {
        Current = new OptiFineViewModel(this);
        ActivePage = "Home";
    }

    public RelayCommand NavLogsCommand { get; private set; } = null!;

    public void ShowLogs()
    {
        (Current as LogsViewModel)?.Detach();
        Current = new LogsViewModel(this);
        ActivePage = "Logs";
    }

    public void ShowComingSoon(string title, string description)
    {
        Current = new ComingSoonViewModel(title, description);
        ActivePage = title;
    }

    public void SignOut(bool keepRememberedEmail)
    {
        Services.Auth.SignOut();
        if (!keepRememberedEmail)
        {
            Services.Settings.Current.RememberedEmail = string.Empty;
            Services.Settings.Current.RememberEmail = false;
            Services.Settings.Save();
        }
        NovaLog.Info("App", "Returning to login screen.");
        ShowLogin();
    }
}
