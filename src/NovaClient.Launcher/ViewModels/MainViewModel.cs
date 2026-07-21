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
    public ViewModelBase? Current { get => _current; private set => Set(ref _current, value); }

    public string LauncherTitle => Services.Branding.LauncherTitle;
    public string VersionText => $"v{Services.Branding.LauncherVersion}";

    // Sidebar navigation
    public RelayCommand NavPlayCommand { get; }
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
        NavSettingsCommand = new RelayCommand(ShowSettings);
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
        if (restoreSession) _ = vm.TryRestoreSessionAsync();
    }

    public void ShowHome()
    {
        var vm = new HomeViewModel(this);
        Current = vm;
        _ = vm.LoadAsync();
    }

    public void ShowSettings() => Current = new SettingsViewModel(this);

    public void ShowOptiFineSetup() => Current = new OptiFineViewModel(this);

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
