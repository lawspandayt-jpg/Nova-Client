using System.Text.Json;
using NovaClient.Core.Logging;
using NovaClient.Core.Security;

namespace NovaClient.Authentication;

/// <summary>
/// Orchestrates the full chain: Microsoft OAuth (PKCE) → Xbox Live → XSTS → Minecraft services →
/// entitlement check → profile. Supports multiple saved accounts: each entry keeps only its MSA
/// refresh token + last Minecraft token, DPAPI-encrypted in a single blob.
/// </summary>
public sealed class AuthenticationService
{
    private const string StoreKey = "accounts";
    private const string LegacyStoreKey = "auth";
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly MicrosoftOAuth _oauth;
    private readonly SecureTokenStore _store;
    private PersistedAccounts _accounts = new();
    private string _msaRefreshToken = "";

    public AuthSession? Session { get; private set; }

    public event Action<string>? StatusChanged;

    public AuthenticationService(string microsoftClientId, SecureTokenStore store)
    {
        _oauth = new MicrosoftOAuth(microsoftClientId);
        _store = store;
        LoadAccounts();
    }

    private void Status(string text)
    {
        NovaLog.Info("Auth", text);
        StatusChanged?.Invoke(text);
    }

    // ------------------------------------------------------------------ account list

    private void LoadAccounts()
    {
        try
        {
            var json = _store.Load(StoreKey);
            if (json is not null)
            {
                _accounts = JsonSerializer.Deserialize<PersistedAccounts>(json) ?? new PersistedAccounts();
                return;
            }
            // Migrate the pre-multi-account single blob.
            var legacy = _store.Load(LegacyStoreKey);
            if (legacy is not null)
            {
                var single = JsonSerializer.Deserialize<PersistedAuth>(legacy);
                if (single is not null && single.MsaRefreshToken.Length > 0)
                {
                    _accounts = new PersistedAccounts { Accounts = { single }, ActiveUuid = single.Uuid };
                    SaveAccounts();
                }
                _store.Delete(LegacyStoreKey);
            }
        }
        catch
        {
            _accounts = new PersistedAccounts();
        }
    }

    private void SaveAccounts() => _store.Save(StoreKey, JsonSerializer.Serialize(_accounts, JsonOptions));

    /// <summary>Accounts available on the login screen (no secrets).</summary>
    public IReadOnlyList<SavedAccount> GetSavedAccounts() =>
        _accounts.Accounts.Select(a => new SavedAccount(a.Uuid, a.Name, a.Email)).ToList();

    /// <summary>Removes one saved account's tokens (login-screen "X" button).</summary>
    public void RemoveAccount(string uuid)
    {
        _accounts.Accounts.RemoveAll(a => a.Uuid == uuid);
        if (_accounts.ActiveUuid == uuid) _accounts.ActiveUuid = "";
        SaveAccounts();
        NovaLog.Info("Auth", "Removed a saved account.");
    }

    // ------------------------------------------------------------------ sign-in

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

    /// <summary>Restores the last active account, refreshing tokens as needed. Null → sign-in required.</summary>
    public Task<AuthSession?> TryRestoreAsync(CancellationToken ct = default) =>
        _accounts.ActiveUuid.Length == 0 ? Task.FromResult<AuthSession?>(null) : ActivateAsync(_accounts.ActiveUuid, ct);

    /// <summary>Signs in to a specific saved account (login-screen account picker).</summary>
    public async Task<AuthSession?> ActivateAsync(string uuid, CancellationToken ct = default)
    {
        var entry = _accounts.Accounts.FirstOrDefault(a => a.Uuid == uuid);
        if (entry is null || entry.MsaRefreshToken.Length == 0) return null;

        _msaRefreshToken = entry.MsaRefreshToken;

        // Reuse a still-valid Minecraft token without a network round-trip.
        if (DateTimeOffset.UtcNow < entry.McTokenExpires - TimeSpan.FromMinutes(10) && entry.Uuid.Length > 0)
        {
            Session = new AuthSession
            {
                Email = entry.Email,
                MinecraftAccessToken = entry.McAccessToken,
                MinecraftTokenExpires = entry.McTokenExpires,
                Profile = new MinecraftProfile(entry.Uuid, entry.Name, entry.SkinUrl),
                OwnsMinecraft = entry.OwnsMinecraft
            };
            _accounts.ActiveUuid = entry.Uuid;
            SaveAccounts();
            Status($"Welcome back, {entry.Name}.");
            return Session;
        }

        try
        {
            Status($"Refreshing session for {entry.Name}…");
            var msa = await _oauth.RefreshAsync(entry.MsaRefreshToken, ct);
            return await RunChainAsync(msa, entry.Email, ct);
        }
        catch (AuthException ex) when (ex.Kind is AuthErrorKind.RefreshExpired or AuthErrorKind.MicrosoftAuthFailed)
        {
            NovaLog.Warn("Auth", "Saved session could not be refreshed; account requires a fresh sign-in.");
            RemoveAccount(uuid);
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
        PersistActive();
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

    private void PersistActive()
    {
        if (Session is null) return;
        var entry = new PersistedAuth
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
        _accounts.Accounts.RemoveAll(a => a.Uuid == entry.Uuid);
        _accounts.Accounts.Insert(0, entry);
        _accounts.ActiveUuid = entry.Uuid;
        SaveAccounts();
    }

    /// <summary>
    /// Signs the CURRENT account out: its stored tokens are deleted and the profile cleared.
    /// Other saved accounts and launcher settings are untouched.
    /// </summary>
    public void SignOut()
    {
        if (Session is not null) RemoveAccount(Session.Profile.Uuid);
        _msaRefreshToken = "";
        Session = null;
        NovaLog.Info("Auth", "Signed out; local tokens for the account removed.");
    }

    /// <summary>
    /// Switch Account: clears the active session but KEEPS the account saved, so the login screen
    /// offers both the saved account list and fresh email entry.
    /// </summary>
    public void Deactivate()
    {
        _accounts.ActiveUuid = "";
        SaveAccounts();
        _msaRefreshToken = "";
        Session = null;
        NovaLog.Info("Auth", "Session deactivated (account kept for quick switch).");
    }
}
