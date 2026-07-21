using System.Diagnostics;
using Microsoft.Win32;
using NovaClient.Core.Logging;
using NovaClient.GameClient;
using NovaClient.Launcher.Common;

namespace NovaClient.Launcher.ViewModels;

public sealed class OptiFineViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly OptiFineService _service;

    private string _selectedFile = "";
    public string SelectedFile { get => _selectedFile; set => Set(ref _selectedFile, value); }

    private string _statusText;
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private bool _statusOk;
    public bool StatusOk { get => _statusOk; set => Set(ref _statusOk, value); }

    private bool _canInstall;
    public bool CanInstall { get => _canInstall; set => Set(ref _canInstall, value); }

    public string RecommendedEdition => _main.Services.Branding.OptiFineVersion;

    public RelayCommand BrowseCommand { get; }
    public RelayCommand InstallCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand OpenOptiFineSiteCommand { get; }

    public OptiFineViewModel(MainViewModel main)
    {
        _main = main;
        _service = main.Services.Launcher.OptiFine;

        var installed = _service.DetectInstalled();
        _statusText = installed is not null
            ? $"OptiFine {installed.Edition} is currently installed. You can replace it with a different 1.8.9 build."
            : $"Download OptiFine {RecommendedEdition} from optifine.net, then select the jar below.";
        _statusOk = installed is not null;

        BrowseCommand = new RelayCommand(Browse);
        InstallCommand = new RelayCommand(Install, () => CanInstall);
        BackCommand = new RelayCommand(() => main.ShowHome());
        OpenOptiFineSiteCommand = new RelayCommand(() =>
            Process.Start(new ProcessStartInfo("https://optifine.net/downloads") { UseShellExecute = true }));
    }

    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "OptiFine jar|*.jar",
            Title = "Select the official OptiFine 1.8.9 jar"
        };
        if (dialog.ShowDialog() != true) return;
        SelectedFile = dialog.FileName;

        var result = _service.Validate(SelectedFile, out var info);
        StatusText = OptiFineService.DescribeValidation(result, info);
        StatusOk = result == OptiFineValidation.Ok;
        CanInstall = result == OptiFineValidation.Ok;
    }

    private void Install()
    {
        try
        {
            var info = _service.Install(SelectedFile);
            StatusText = $"OptiFine {info.Edition} installed successfully. It will load automatically on every launch.";
            StatusOk = true;
            CanInstall = false;
            _main.ShowHome();
        }
        catch (Exception ex)
        {
            NovaLog.Error("OptiFine", "Install failed", ex);
            StatusText = "OptiFine installation failed: " + ex.Message;
            StatusOk = false;
        }
    }
}
