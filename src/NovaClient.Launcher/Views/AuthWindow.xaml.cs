using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using NovaClient.Authentication;
using NovaClient.Core.Logging;

namespace NovaClient.Launcher.Views;

/// <summary>
/// Hosts Microsoft's official sign-in page in WebView2. The window only watches for the
/// OAuth redirect URI — it never injects JavaScript, reads fields, or captures keystrokes.
/// </summary>
public partial class AuthWindow : Window
{
    private readonly string _startUrl;
    private readonly string _userDataFolder;

    /// <summary>The nativeclient redirect (contains ?code= or ?error=), null when cancelled.</summary>
    public Uri? ResultUri { get; private set; }

    /// <summary>True when the WebView2 runtime is missing/broken → caller uses the browser fallback.</summary>
    public bool WebViewUnavailable { get; private set; }

    public AuthWindow(string startUrl, string cacheDirectory)
    {
        InitializeComponent();
        _startUrl = startUrl;
        _userDataFolder = Path.Combine(cacheDirectory, "webview2");
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            await WebView.EnsureCoreWebView2Async(environment);

            var settings = WebView.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;

            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.NavigationCompleted += (_, _) => LoadingPanel.Visibility = Visibility.Collapsed;
            WebView.Source = new Uri(_startUrl);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or DllNotFoundException or IOException or InvalidOperationException)
        {
            NovaLog.Warn("AuthWindow", $"WebView2 could not start: {ex.GetType().Name}: {ex.Message}");
            WebViewUnavailable = true;
            Close();
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!e.Uri.StartsWith(MicrosoftOAuth.NativeClientRedirect, StringComparison.OrdinalIgnoreCase)) return;
        ResultUri = new Uri(e.Uri);
        e.Cancel = true;
        Close();
    }
}
