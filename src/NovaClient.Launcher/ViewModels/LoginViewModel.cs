using System.Windows;
using NovaClient.Authentication;
using NovaClient.Core.Logging;
using NovaClient.Core.Util;
using NovaClient.Launcher.Common;
using NovaClient.Launcher.Views;

namespace NovaClient.Launcher.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    private string _email = "";
    public string Email { get => _email; set { if (Set(ref _email, value)) ErrorText = ""; } }

    private bool _rememberEmail;
    public bool RememberEmail { get => _rememberEmail; set => Set(ref _rememberEmail, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private string _errorText = "";
    public string ErrorText { get => _errorText; set => Set(ref _errorText, value); }

    public string ClientName => _main.Services.Branding.ClientName;
    public string VersionText => $"Launcher v{_main.Services.Branding.LauncherVersion} · Minecraft {_main.Services.Branding.MinecraftVersion}";
    public string OwnershipNote => "A Microsoft account that owns Minecraft: Java Edition is required.";
    public string PrivacyNote => "You sign in on Microsoft's official page. This launcher never sees or stores your password, and tokens are stored encrypted on this PC only.";

    public AsyncRelayCommand ContinueCommand { get; }
    public RelayCommand SettingsCommand { get; }

    public LoginViewModel(MainViewModel main)
    {
        _main = main;
        var settings = main.Services.Settings.Current;
        _rememberEmail = settings.RememberEmail;
        _email = settings.RememberEmail ? settings.RememberedEmail : "";
        ContinueCommand = new AsyncRelayCommand(ContinueAsync, () => !IsBusy);
        SettingsCommand = new RelayCommand(main.ShowSettings);
    }

    /// <summary>Silently restores a cached session at startup (token refresh included).</summary>
    public async Task TryRestoreSessionAsync()
    {
        IsBusy = true;
        StatusText = "Checking for a saved session…";
        try
        {
            var session = await _main.Services.Auth.TryRestoreAsync();
            if (session is not null) { _main.ShowHome(); return; }
            StatusText = "";
        }
        catch (Exception ex)
        {
            NovaLog.Warn("Login", $"Session restore failed: {ex.Message}");
            StatusText = "";
        }
        finally { IsBusy = false; }
    }

    private async Task ContinueAsync()
    {
        ErrorText = "";
        var email = Email.Trim();
        if (!EmailMasker.LooksValid(email))
        {
            ErrorText = new AuthException(AuthErrorKind.InvalidEmail, "").UserMessage;
            return;
        }
        if (!_main.Services.Branding.HasValidClientId)
        {
            ErrorText = new AuthException(AuthErrorKind.InvalidClientId, "").UserMessage;
            return;
        }

        PersistRememberedEmail(email);
        IsBusy = true;
        try
        {
            var auth = _main.Services.Auth;
            var pkce = auth.BeginLogin();

            StatusText = "Opening Microsoft sign-in…";
            Uri redirect;
            string redirectUri;

            var webViewResult = TrySignInWithWebView(auth, pkce, email);
            if (webViewResult.Available)
            {
                if (webViewResult.Redirect is null)
                    throw new AuthException(AuthErrorKind.UserCancelled, "Sign-in window was closed.");
                redirect = webViewResult.Redirect;
                redirectUri = MicrosoftOAuth.NativeClientRedirect;
            }
            else
            {
                // WebView2 unavailable → official flow in the user's default browser (RFC 8252).
                StatusText = "Sign in using the browser window that just opened…";
                using var listener = new BrowserAuthListener();
                redirectUri = listener.RedirectUri;
                var url = auth.BuildAuthorizeUrl(pkce, redirectUri, email);
                BrowserAuthListener.OpenInSystemBrowser(url);
                redirect = await listener.WaitForCallbackAsync(TimeSpan.FromMinutes(5));
            }

            StatusText = "Completing sign-in…";
            auth.StatusChanged += OnAuthStatus;
            try
            {
                await auth.CompleteLoginAsync(redirect, pkce, redirectUri, email);
            }
            finally
            {
                auth.StatusChanged -= OnAuthStatus;
            }
            _main.ShowHome();
        }
        catch (AuthException ex)
        {
            NovaLog.Warn("Login", $"Sign-in failed: {ex.Kind}: {ex.Message}");
            ErrorText = ex.UserMessage;
            StatusText = "";
        }
        catch (Exception ex)
        {
            NovaLog.Error("Login", "Unexpected sign-in error", ex);
            ErrorText = "Sign-in failed due to an unexpected error. See the launcher log for details.";
            StatusText = "";
        }
        finally { IsBusy = false; }
    }

    private void OnAuthStatus(string status) =>
        Application.Current.Dispatcher.Invoke(() => StatusText = status);

    private (bool Available, Uri? Redirect) TrySignInWithWebView(AuthenticationService auth, PkceSession pkce, string email)
    {
        var url = auth.BuildAuthorizeUrl(pkce, MicrosoftOAuth.NativeClientRedirect, email);
        var window = new AuthWindow(url, _main.Services.Paths.Cache) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        if (window.WebViewUnavailable)
        {
            NovaLog.Warn("Login", "WebView2 runtime unavailable; falling back to the system browser.");
            return (false, null);
        }
        return (true, window.ResultUri);
    }

    private void PersistRememberedEmail(string email)
    {
        var settings = _main.Services.Settings;
        settings.Current.RememberEmail = RememberEmail;
        settings.Current.RememberedEmail = RememberEmail ? email : string.Empty;
        settings.Save();
    }
}
