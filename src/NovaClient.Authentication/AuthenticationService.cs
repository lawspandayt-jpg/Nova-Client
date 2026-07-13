using System.Text.Json;
using NovaClient.Core.Logging;
using NovaClient.Core.Security;

namespace NovaClient.Authentication;

/// <summary>
/// Orchestrates the full chain: Microsoft OAuth (PKCE) → Xbox Live → XSTS → Minecraft services →
/// entitlement check → profile. Persists only the MSA refresh token + Minecraft token (DPAPI).
/// </summary>
public sealed class AuthenticationService
{
    private const string StoreKey = "auth";
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly MicrosoftOAuth _oauth;
    private readonly SecureTokenStore _store;
    private string _msaRefreshToken = "";

    public AuthSession? Session { get; private set; }

    public event Action<string>? StatusChanged;

    public AuthenticationService(string microsoftClientId, SecureTokenStore store)
    {
        _oauth = new MicrosoftOAuth(microsoftClientId);
        _store = store;
    }

    private void Status(string text)
    {
        NovaLog.Info("Auth", text);
        StatusChanged?.Invoke(text);
    }

    public PkceSession BeginLogin() => MicrosoftOAuth.CreatePkceSession();

    public string BuildAuthorizeUrl(PkceSession pkce, string redirectUri, string email, bool forceAccountSelection = false) =>
        _oauth.BuildAuthorizeUrl(pkce, redirectUri, email, forceAccountSelection);

    /// <summary>Completes sign-in after Microsoft redirected back with an authorization code.</summary>
    public async Task<AuthSession> CompleteLoginAsync(Uri redirect, PkceSession pkce, string redirectUri, string email, CancellationToken ct = default)
    {
        var code = MicrosoftOAuth.ExtractCode(redirect, pkce);
        Status("Exchanging authorization code…");
        var msa = await _oauth.ExchangeCodeAsync(code, pkce.CodeVerifier, redirectUri, ct);
        return await RunChainAsync(msa, email, ct);
    }

    /// <summary>Restores the cached session, refreshing tokens as needed. Null → sign-in required.</summary>
    public async Task<AuthSession?> TryRestoreAsync(CancellationToken ct = default)
    {
        var json = _store.Load(StoreKey);
        if (json is null) return null;

        PersistedAuth? persisted;
        try { persisted = JsonSerializer.Deserialize<PersistedAuth>(json); }
        catch { _store.Delete(StoreKey); return null; }
        if (persisted is null || string.IsNullOrEmpty(persisted.MsaRefreshToken)) return null;

        _msaRefreshToken = persisted.MsaRefreshToken;

        // Reuse a still-valid Minecraft token without a network round-trip.
        if (DateTimeOffset.UtcNow < persisted.McTokenExpires - TimeSpan.FromMinutes(10) && persisted.Uuid.Length > 0)
        {
            Session = new AuthSession
            {
                Email = persisted.Email,
                MinecraftAccessToken = persisted.McAccessToken,
                MinecraftTokenExpires = persisted.McTokenExpires,
                Profile = new MinecraftProfile(persisted.Uuid, persisted.Name, persisted.SkinUrl),
                OwnsMinecraft = persisted.OwnsMinecraft
            };
            Status($"Welcome back, {persisted.Name}.");
            return Session;
        }

        try
        {
            Status("Refreshing Microsoft session…");
            var msa = await _oauth.RefreshAsync(persisted.MsaRefreshToken, ct);
            return await RunChainAsync(msa, persisted.Email, ct);
        }
        catch (AuthException ex) when (ex.Kind is AuthErrorKind.RefreshExpired or AuthErrorKind.MicrosoftAuthFailed)
        {
            NovaLog.Warn("Auth", "Saved session could not be refreshed; sign-in required.");
            SignOut();
            return null;
        }
    }

    private async Task<AuthSession> RunChainAsync(MsaTokens msa, string email, CancellationToken ct)
    {
        _msaRefreshToken = msa.RefreshToken;

        Status("Signing in to Xbox Live…");
        var xbox = await XboxAuth.AuthenticateAsync(msa.AccessToken, ct);

        Status("Signing in to Minecraft services…");
        var mc = await MinecraftAuth.LoginWithXboxAsync(xbox, ct);

        Status("Checking Minecraft: Java Edition ownership…");
        var entitled = await MinecraftAuth.HasJavaEntitlementAsync(mc.AccessToken, ct);

        Status("Loading Minecraft profile…");
        var profile = await MinecraftAuth.GetProfileAsync(mc.AccessToken, ct);

        if (profile is null)
        {
            throw entitled
                ? new AuthException(AuthErrorKind.ProfileNotFound, "Java profile has not been created yet.")
                : new AuthException(AuthErrorKind.NotOwned, "Account does not own Minecraft: Java Edition.");
        }

        Session = new AuthSession
        {
            Email = email,
            MinecraftAccessToken = mc.AccessToken,
            MinecraftTokenExpires = mc.Expires,
            Profile = profile,
            // A Java profile can only exist for accounts with Java access — covers Game Pass,
            // whose entitlement list may be empty.
            OwnsMinecraft = true
        };
        Persist();
        Status($"Signed in as {profile.Name}.");
        return Session;
    }

    /// <summary>Ensures the Minecraft token is valid right before launch, refreshing if necessary.</summary>
    public async Task<AuthSession> EnsureFreshAsync(CancellationToken ct = default)
    {
        if (Session is null) throw new AuthException(AuthErrorKind.TokenExpired, "Not signed in.");
        if (Session.MinecraftTokenValid) return Session;
        if (string.IsNullOrEmpty(_msaRefreshToken))
            throw new AuthException(AuthErrorKind.RefreshExpired, "No refresh token available.");
        var msa = await _oauth.RefreshAsync(_msaRefreshToken, ct);
        return await RunChainAsync(msa, Session.Email, ct);
    }

    private void Persist()
    {
        if (Session is null) return;
        var persisted = new PersistedAuth
        {
            Email = Session.Email,
            MsaRefreshToken = _msaRefreshToken,
            McAccessToken = Session.MinecraftAccessToken,
            McTokenExpires = Session.MinecraftTokenExpires,
            Uuid = Session.Profile.Uuid,
            Name = Session.Profile.Name,
            SkinUrl = Session.Profile.SkinUrl,
            OwnsMinecraft = Session.OwnsMinecraft
        };
        _store.Save(StoreKey, JsonSerializer.Serialize(persisted, JsonOptions));
    }

    /// <summary>Removes stored tokens and the active profile. Launcher settings are untouched.</summary>
    public void SignOut()
    {
        _store.Delete(StoreKey);
        _msaRefreshToken = "";
        Session = null;
        NovaLog.Info("Auth", "Signed out; local tokens removed.");
    }
}
