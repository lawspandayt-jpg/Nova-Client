using System.Net;
using System.Text;
using System.Text.Json;
using NovaClient.Core.Http;
using NovaClient.Core.Logging;

namespace NovaClient.Authentication;

public sealed record MinecraftToken(string AccessToken, DateTimeOffset Expires);

/// <summary>Minecraft services: sign-in with XSTS, entitlement (ownership) check, profile fetch.</summary>
public static class MinecraftAuth
{
    private const string LoginEndpoint = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string EntitlementsEndpoint = "https://api.minecraftservices.com/entitlements/mcstore";
    private const string ProfileEndpoint = "https://api.minecraftservices.com/minecraft/profile";

    public static async Task<MinecraftToken> LoginWithXboxAsync(XboxTokens xbox, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            identityToken = $"XBL3.0 x={xbox.UserHash};{xbox.XstsToken}"
        });

        HttpResponseMessage response;
        try
        {
            response = await HttpProvider.Client.PostAsync(LoginEndpoint,
                new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AuthException(AuthErrorKind.NoInternet, "Could not reach Minecraft services.", ex);
        }

        if ((int)response.StatusCode == 429)
            throw new AuthException(AuthErrorKind.RateLimited, "Minecraft services rate limit reached.");
        if ((int)response.StatusCode == 403)
            // Xbox/XSTS succeeded but Mojang's API refused the app: the launcher's client ID has
            // not (yet) been approved by Mojang for the Minecraft services API.
            throw new AuthException(AuthErrorKind.ClientIdNotApproved, "Minecraft login returned HTTP 403 (client ID not Mojang-approved).");
        if (!response.IsSuccessStatusCode)
            throw new AuthException(AuthErrorKind.ServicesUnavailable, $"Minecraft login returned HTTP {(int)response.StatusCode}.");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token = doc.RootElement.GetProperty("access_token").GetString()!;
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 86400;
        NovaLog.Info("Minecraft", "Minecraft services token obtained.");
        return new MinecraftToken(token, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    /// <summary>
    /// True when the store entitlements list Java Edition. Game Pass accounts can report an empty
    /// list here yet still have a Java profile, so the caller treats "entitlements empty but
    /// profile exists" as owned — never the other way around.
    /// </summary>
    public static async Task<bool> HasJavaEntitlementAsync(string mcAccessToken, CancellationToken ct = default)
    {
        using var doc = await GetAsync(EntitlementsEndpoint, mcAccessToken, ct)
            ?? throw new AuthException(AuthErrorKind.ServicesUnavailable, "Entitlement check failed.");
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var item in items.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is "product_minecraft" or "game_minecraft") return true;
        }
        return false;
    }

    /// <summary>Returns null when the account has no Java Edition profile (HTTP 404).</summary>
    public static async Task<MinecraftProfile?> GetProfileAsync(string mcAccessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ProfileEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcAccessToken);

        HttpResponseMessage response;
        try
        {
            response = await HttpProvider.Client.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AuthException(AuthErrorKind.NoInternet, "Could not reach Minecraft services.", ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new AuthException(AuthErrorKind.ServicesUnavailable, $"Profile request returned HTTP {(int)response.StatusCode}.");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var uuid = root.GetProperty("id").GetString()!;
        var name = root.GetProperty("name").GetString()!;
        string? skinUrl = null;
        if (root.TryGetProperty("skins", out var skins) && skins.ValueKind == JsonValueKind.Array)
        {
            foreach (var skin in skins.EnumerateArray())
            {
                var state = skin.TryGetProperty("state", out var s) ? s.GetString() : null;
                if (state == "ACTIVE") { skinUrl = skin.GetProperty("url").GetString(); break; }
                skinUrl ??= skin.TryGetProperty("url", out var u) ? u.GetString() : null;
            }
        }
        NovaLog.Info("Minecraft", $"Profile loaded for '{name}'.");
        return new MinecraftProfile(uuid, name, skinUrl);
    }

    private static async Task<JsonDocument?> GetAsync(string url, string bearer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        try
        {
            var response = await HttpProvider.Client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException ex)
        {
            throw new AuthException(AuthErrorKind.NoInternet, "Could not reach Minecraft services.", ex);
        }
    }
}
