using System.Windows;
using System.Windows.Media;
using NovaClient.Core.Logging;
using NovaClient.Launcher.Services;
using NovaClient.Launcher.ViewModels;

namespace NovaClient.Launcher;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            NovaLog.Error("App", "Unhandled UI exception", args.Exception);
            MessageBox.Show(args.Exception.Message, "Nova Client — unexpected error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        Services = new AppServices();

        // Apply the accent color from branding.json to the theme at runtime.
        try
        {
            var accent = (Color)ColorConverter.ConvertFromString(Services.Branding.AccentColor);
            Resources["AccentBrush"] = new SolidColorBrush(accent);
        }
        catch { /* invalid color string in branding.json — keep the default */ }

        var window = new MainWindow { DataContext = new MainViewModel(Services) };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        NovaLog.Info("App", "Launcher closing.");
        NovaLog.Shutdown();
        base.OnExit(e);
    }
}
