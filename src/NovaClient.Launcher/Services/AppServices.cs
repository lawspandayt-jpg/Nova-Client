using System.IO;
using NovaClient.Authentication;
using NovaClient.Core;
using NovaClient.Core.Branding;
using NovaClient.Core.Logging;
using NovaClient.Core.Security;
using NovaClient.Core.Settings;
using NovaClient.GameClient;
using NovaClient.Updater;

namespace NovaClient.Launcher.Services;

/// <summary>Composition root: builds the object graph once at startup.</summary>
public sealed class AppServices
{
    public BrandingConfig Branding { get; }
    public NovaPaths Paths { get; }
    public SettingsService Settings { get; }
    public SecureTokenStore TokenStore { get; }
    public AuthenticationService Auth { get; }
    public SkinService Skins { get; }
    public NovaLauncher Launcher { get; }
    public UpdateService Updater { get; }
    public FriendsService Friends { get; }
    public PresenceService Presence { get; }

    public AppServices()
    {
        WriteDefaultBrandingIfMissing();
        Branding = BrandingConfig.Load();
        Settings = new SettingsService(new NovaPaths().Config);
        Paths = new NovaPaths(Settings.Current.GameDirectory);
        Paths.EnsureCreated();
        NovaLog.Init(Paths.Logs);
        NovaLog.Info("App", $"{Branding.LauncherTitle} v{Branding.LauncherVersion} starting.");

        TokenStore = new SecureTokenStore(Paths.SecureStore);
        Auth = new AuthenticationService(Branding.MicrosoftClientId, TokenStore);
        Skins = new SkinService(Paths.Cache);
        Launcher = new NovaLauncher(Paths, Branding.GameClientVersion);
        Launcher.SetBranding(Branding.ClientName, Branding.AccentColor);
        Updater = new UpdateService(Branding.UpdateApiUrl, AppContext.BaseDirectory, Paths.Cache);
        Friends = new FriendsService(Paths.Config);
        Presence = new PresenceService(Branding.PresenceApiUrl);

        ExtractEmbeddedClientJar();
    }

    /// <summary>
    /// The single-file exe carries nova-client.jar as an embedded resource; unpack it into
    /// %AppData%\NovaClient\client so the game bootstrap can use it.
    /// </summary>
    private void ExtractEmbeddedClientJar()
    {
        try
        {
            using var resource = typeof(AppServices).Assembly
                .GetManifestResourceStream("NovaClient.Launcher.nova-client.jar");
            if (resource is null)
            {
                NovaLog.Warn("App", "No embedded nova-client.jar in this build (client-java was not built before publishing).");
                return;
            }
            var target = Path.Combine(Paths.ClientFiles, "nova-client.jar");
            using var file = File.Create(target);
            resource.CopyTo(file);
            NovaLog.Info("App", "Embedded nova-client.jar extracted.");
        }
        catch (Exception ex)
        {
            NovaLog.Error("App", "Could not extract embedded nova-client.jar", ex);
        }
    }

    /// <summary>
    /// Keeps branding editable even for the single-file exe: if no branding.json exists next to
    /// the executable, write the default template so users can set their Microsoft client ID.
    /// </summary>
    private static void WriteDefaultBrandingIfMissing()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "branding.json");
            if (File.Exists(path)) return;
            var json = System.Text.Json.JsonSerializer.Serialize(new BrandingConfig(),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Read-only install dir — defaults from code are used instead.
        }
    }
}
