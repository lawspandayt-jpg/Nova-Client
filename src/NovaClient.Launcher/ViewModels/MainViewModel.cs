using System.Diagnostics;
using System.IO;
using NovaClient.Core.Logging;
using NovaClient.Launcher.Common;
using NovaClient.Launcher.Services;

namespace NovaClient.Launcher.ViewModels;

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
        ShowLogin(restoreSession: true);
    }

    public void ShowLogin(bool restoreSession = false)
    {
        var vm = new LoginViewModel(this);
        Current = vm;
        ActivePage = "Home";
        if (restoreSession) _ = vm.TryRestoreSessionAsync();
    }

    public void ShowHome()
    {
        var vm = new HomeViewModel(this);
        Current = vm;
        ActivePage = "Home";
        OnPropertyChanged(nameof(SelectedVersionText));
        _ = vm.LoadAsync();
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
