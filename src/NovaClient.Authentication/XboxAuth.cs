using System.Text;
using System.Text.Json;
using NovaClient.Core.Http;
using NovaClient.Core.Logging;

namespace NovaClient.Authentication;

public sealed record XboxTokens(string XstsToken, string UserHash);

/// <summary>Xbox Live user authentication followed by XSTS authorization for Minecraft services.</summary>
public static class XboxAuth
{
    private const string UserAuthEndpoint = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsEndpoint = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftRelyingParty = "rp://api.minecraftservices.com/";

    public static async Task<XboxTokens> AuthenticateAsync(string msaAccessToken, CancellationToken ct = default)
    {
        // Step 1: Xbox Live user token from the Microsoft access token.
        var userPayload = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + msaAccessToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };
        var userRoot = await PostJsonAsync(UserAuthEndpoint, userPayload, AuthErrorKind.XboxAuthFailed, ct);
        var userToken = userRoot.RootElement.GetProperty("Token").GetString()!;
        var userHash = userRoot.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()!;
        userRoot.Dispose();
        NovaLog.Info("Xbox", "Xbox Live user token obtained.");

        // Step 2: XSTS token scoped to Minecraft services.
        var xstsPayload = new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { userToken } },
            RelyingParty = MinecraftRelyingParty,
            TokenType = "JWT"
        };
        var xstsRoot = await PostJsonAsync(XstsEndpoint, xstsPayload, AuthErrorKind.XstsFailed, ct);
        var xstsToken = xstsRoot.RootElement.GetProperty("Token").GetString()!;
        xstsRoot.Dispose();
        NovaLog.Info("Xbox", "XSTS token obtained.");

        return new XboxTokens(xstsToken, userHash);
    }

    private static async Task<JsonDocument> PostJsonAsync(string url, object payload, AuthErrorKind failureKind, CancellationToken ct)
    {
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Add("x-xbl-contract-version", "1");

        HttpResponseMessage response;
        try
        {
            response = await HttpProvider.Client.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AuthException(AuthErrorKind.NoInternet, "Could not reach Xbox Live.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode) return JsonDocument.Parse(body);

        if ((int)response.StatusCode == 401 && TryGetXErr(body, out var xerr))
            throw MapXErr(xerr);
        throw new AuthException(failureKind, $"Xbox endpoint returned HTTP {(int)response.StatusCode}.");
    }

    private static bool TryGetXErr(string body, out long xerr)
    {
        xerr = 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("XErr", out var el)) { xerr = el.GetInt64(); return true; }
        }
        catch { }
        return false;
    }

    /// <summary>Documented XSTS error codes for consumer accounts.</summary>
    private static AuthException MapXErr(long xerr) => xerr switch
    {
        2148916233 => new AuthException(AuthErrorKind.NoXboxProfile, "Account has no Xbox profile (XErr 2148916233)."),
        2148916235 => new AuthException(AuthErrorKind.RegionOrFamilyRestriction, "Xbox Live is banned/unavailable in this region (XErr 2148916235)."),
        2148916236 or 2148916237 => new AuthException(AuthErrorKind.RegionOrFamilyRestriction, $"Account needs adult verification (XErr {xerr})."),
        2148916238 => new AuthException(AuthErrorKind.ChildAccount, "Child account must be added to a Microsoft family by an adult (XErr 2148916238)."),
        _ => new AuthException(AuthErrorKind.XstsFailed, $"XSTS rejected the sign-in (XErr {xerr}).")
    };
}
