using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NovaClient.Core.Http;
using NovaClient.Core.Logging;

namespace NovaClient.Authentication;

public sealed record MsaTokens(string AccessToken, string RefreshToken, DateTimeOffset Expires);

/// <summary>
/// Official Microsoft identity platform OAuth 2.0 authorization-code flow with PKCE (RFC 7636)
/// for a public client. No client secret exists anywhere in the launcher. The launcher only ever
/// sees the authorization code and tokens — the password stays on Microsoft's page.
/// </summary>
public sealed class MicrosoftOAuth
{
    public const string AuthorizeEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
    public const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

    /// <summary>Standard redirect for WebView2 (navigation interception, no local server needed).</summary>
    public const string NativeClientRedirect = "https://login.microsoftonline.com/common/oauth2/nativeclient";

    public const string Scope = "XboxLive.signin offline_access";

    private readonly string _clientId;

    public MicrosoftOAuth(string clientId) => _clientId = clientId;

    public static PkceSession CreatePkceSession()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        return new PkceSession(verifier, challenge, state);
    }

    public string BuildAuthorizeUrl(PkceSession pkce, string redirectUri, string? loginHintEmail, bool forceAccountSelection = false)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["scope"] = Scope,
            ["state"] = pkce.State,
            ["code_challenge"] = pkce.CodeChallenge,
            ["code_challenge_method"] = "S256",
        };
        if (!string.IsNullOrWhiteSpace(loginHintEmail)) query["login_hint"] = loginHintEmail!.Trim();
        if (forceAccountSelection) query["prompt"] = "select_account";
        return AuthorizeEndpoint + "?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }

    /// <summary>Parses the redirect back from Microsoft; validates state before touching the code.</summary>
    public static string ExtractCode(Uri redirect, PkceSession pkce)
    {
        var query = System.Web.HttpUtility.ParseQueryString(redirect.Query);
        var error = query["error"];
        if (error is not null)
        {
            var description = query["error_description"] ?? error;
            if (error == "access_denied")
                throw new AuthException(AuthErrorKind.UserCancelled, "User declined the sign-in request.");
            throw new AuthException(AuthErrorKind.MicrosoftAuthFailed, $"Microsoft returned '{error}': {description}");
        }
        var state = query["state"];
        if (!string.Equals(state, pkce.State, StringComparison.Ordinal))
            throw new AuthException(AuthErrorKind.StateMismatch, "OAuth state did not match the value this launcher generated.");
        var code = query["code"];
        if (string.IsNullOrEmpty(code))
            throw new AuthException(AuthErrorKind.MicrosoftAuthFailed, "Microsoft redirect contained no authorization code.");
        return code!;
    }

    public Task<MsaTokens> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct = default) =>
        RequestTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["scope"] = Scope,
        }, ct);

    public Task<MsaTokens> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        RequestTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope,
        }, ct);

    private static async Task<MsaTokens> RequestTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await HttpProvider.Client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AuthException(AuthErrorKind.NoInternet, "Could not reach Microsoft's token service.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var kind = ClassifyTokenError(body, (int)response.StatusCode, form["grant_type"]);
            NovaLog.Warn("MSA", $"Token endpoint returned {(int)response.StatusCode} ({kind}).");
            throw new AuthException(kind, $"Microsoft token request failed with HTTP {(int)response.StatusCode}.");
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var access = root.GetProperty("access_token").GetString()!;
        var refresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString()! : "";
        var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
        NovaLog.Info("MSA", "Microsoft token obtained.");
        return new MsaTokens(access, refresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private static AuthErrorKind ClassifyTokenError(string body, int status, string grantType)
    {
        if (status == 429) return AuthErrorKind.RateLimited;
        if (body.Contains("unauthorized_client") || body.Contains("invalid_client") || body.Contains("AADSTS700016"))
            return AuthErrorKind.InvalidClientId;
        if (body.Contains("invalid_grant"))
            return grantType == "refresh_token" ? AuthErrorKind.RefreshExpired : AuthErrorKind.PkceFailed;
        return AuthErrorKind.MicrosoftAuthFailed;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
