using System.Text.Json.Serialization;

namespace NovaClient.Authentication;

/// <summary>One PKCE round: verifier/challenge/state for a single authorization attempt.</summary>
public sealed record PkceSession(string CodeVerifier, string CodeChallenge, string State);

public sealed record MinecraftProfile(string Uuid, string Name, string? SkinUrl);

/// <summary>Fully authenticated session. Access tokens live only in memory and the DPAPI store.</summary>
public sealed class AuthSession
{
    public required string Email { get; init; }
    public required string MinecraftAccessToken { get; set; }
    public required DateTimeOffset MinecraftTokenExpires { get; set; }
    public required MinecraftProfile Profile { get; set; }
    public bool OwnsMinecraft { get; set; }

    public bool MinecraftTokenValid => DateTimeOffset.UtcNow < MinecraftTokenExpires - TimeSpan.FromMinutes(5);
}

/// <summary>All saved accounts (DPAPI-encrypted at rest as config/secure/accounts.bin).</summary>
public sealed class PersistedAccounts
{
    [JsonPropertyName("accounts")] public List<PersistedAuth> Accounts { get; set; } = new();
    [JsonPropertyName("activeUuid")] public string ActiveUuid { get; set; } = "";
}

/// <summary>Non-secret account facts for pickers (no tokens).</summary>
public sealed record SavedAccount(string Uuid, string Name, string Email);

/// <summary>Shape of one DPAPI-encrypted account entry. Never stored in plain text.</summary>
public sealed class PersistedAuth
{
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("msaRefreshToken")] public string MsaRefreshToken { get; set; } = "";
    [JsonPropertyName("mcAccessToken")] public string McAccessToken { get; set; } = "";
    [JsonPropertyName("mcTokenExpires")] public DateTimeOffset McTokenExpires { get; set; }
    [JsonPropertyName("uuid")] public string Uuid { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("skinUrl")] public string? SkinUrl { get; set; }
    [JsonPropertyName("ownsMinecraft")] public bool OwnsMinecraft { get; set; }
}

public enum AuthErrorKind
{
    InvalidEmail,
    UserCancelled,
    WebViewUnavailable,
    BrowserUnavailable,
    MicrosoftAuthFailed,
    StateMismatch,
    PkceFailed,
    TokenExpired,
    RefreshExpired,
    XboxAuthFailed,
    NoXboxProfile,
    XstsFailed,
    ChildAccount,
    RegionOrFamilyRestriction,
    NotOwned,
    ProfileNotFound,
    ServicesUnavailable,
    NoInternet,
    RateLimited,
    InvalidClientId,
    ClientIdNotApproved,
    SkinUnavailable,
    Unknown
}

public sealed class AuthException : Exception
{
    public AuthErrorKind Kind { get; }

    public AuthException(AuthErrorKind kind, string message, Exception? inner = null)
        : base(message, inner) => Kind = kind;

    /// <summary>Short, user-facing explanation for every failure mode in the chain.</summary>
    public string UserMessage => Kind switch
    {
        AuthErrorKind.InvalidEmail => "Please enter a valid Microsoft email address.",
        AuthErrorKind.UserCancelled => "Sign-in was cancelled.",
        AuthErrorKind.WebViewUnavailable => "The secure sign-in window could not be opened. Trying your web browser instead…",
        AuthErrorKind.BrowserUnavailable => "No web browser could be opened for sign-in.",
        AuthErrorKind.MicrosoftAuthFailed => "Microsoft sign-in failed. Please try again.",
        AuthErrorKind.StateMismatch => "Sign-in was rejected for security reasons (state mismatch). Please try again.",
        AuthErrorKind.PkceFailed => "Sign-in was rejected for security reasons (PKCE validation). Please try again.",
        AuthErrorKind.TokenExpired => "Your session expired. Please sign in again.",
        AuthErrorKind.RefreshExpired => "Your saved session expired. Please sign in again.",
        AuthErrorKind.XboxAuthFailed => "Xbox Live sign-in failed. Please try again later.",
        AuthErrorKind.NoXboxProfile => "This Microsoft account has no Xbox profile. Sign in at xbox.com once, then try again.",
        AuthErrorKind.XstsFailed => "Xbox security verification (XSTS) failed. Please try again later.",
        AuthErrorKind.ChildAccount => "This account is a child account. An adult in your Microsoft family must allow it to sign in to third-party apps.",
        AuthErrorKind.RegionOrFamilyRestriction => "Xbox Live is not available for this account (region or family settings restriction).",
        AuthErrorKind.NotOwned => "This Microsoft account does not own Minecraft: Java Edition. Java Edition is required to play.",
        AuthErrorKind.ProfileNotFound => "This account owns Minecraft but has no Java Edition profile yet. Start the official Minecraft Launcher once to create your profile, then try again.",
        AuthErrorKind.ServicesUnavailable => "Minecraft services are currently unavailable. Please try again later.",
        AuthErrorKind.NoInternet => "No internet connection. Please check your network and try again.",
        AuthErrorKind.RateLimited => "Too many sign-in attempts. Please wait a moment and try again.",
        AuthErrorKind.InvalidClientId => "This launcher build has no valid Microsoft client ID configured. See docs/microsoft-app-registration.md.",
        AuthErrorKind.ClientIdNotApproved => "Microsoft and Xbox sign-in succeeded, but Mojang has not approved this launcher's app ID for the Minecraft API yet. Submit it at aka.ms/mce-reviewappid and try again once approved.",
        AuthErrorKind.SkinUnavailable => "Signed in, but the skin preview could not be loaded.",
        _ => "Sign-in failed due to an unexpected error."
    };
}
