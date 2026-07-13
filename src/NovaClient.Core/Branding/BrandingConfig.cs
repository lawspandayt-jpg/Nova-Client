using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaClient.Core.Branding;

/// <summary>
/// All brand-specific values (name, colors, URLs, client ID, versions) live in branding.json
/// next to the executable so the client can be re-branded without recompiling.
/// </summary>
public sealed class BrandingConfig
{
    [JsonPropertyName("clientName")] public string ClientName { get; set; } = "Nova Client";
    [JsonPropertyName("launcherTitle")] public string LauncherTitle { get; set; } = "Nova Client Launcher";
    [JsonPropertyName("clientLogo")] public string ClientLogo { get; set; } = "Assets/logo.png";
    [JsonPropertyName("launcherLogo")] public string LauncherLogo { get; set; } = "Assets/logo.png";
    [JsonPropertyName("accentColor")] public string AccentColor { get; set; } = "#7C5CFF";
    [JsonPropertyName("websiteUrl")] public string WebsiteUrl { get; set; } = "https://example.com";
    [JsonPropertyName("discordUrl")] public string DiscordUrl { get; set; } = "https://discord.gg/example";
    [JsonPropertyName("supportUrl")] public string SupportUrl { get; set; } = "https://example.com/support";
    [JsonPropertyName("privacyUrl")] public string PrivacyUrl { get; set; } = "https://example.com/privacy";
    [JsonPropertyName("updateApiUrl")] public string UpdateApiUrl { get; set; } = "https://example.com/updates/manifest.json";
    [JsonPropertyName("launcherVersion")] public string LauncherVersion { get; set; } = "1.0.0";
    [JsonPropertyName("gameClientVersion")] public string GameClientVersion { get; set; } = "1.0.0";
    [JsonPropertyName("minecraftVersion")] public string MinecraftVersion { get; set; } = "1.8.9";
    [JsonPropertyName("microsoftClientId")] public string MicrosoftClientId { get; set; } = "REPLACE_WITH_REAL_CLIENT_ID";
    [JsonPropertyName("optifineVersion")] public string OptiFineVersion { get; set; } = "1.8.9_HD_U_M5";
    [JsonPropertyName("defaultRamMb")] public int DefaultRamMb { get; set; } = 2048;
    [JsonPropertyName("defaultGameDirectory")] public string? DefaultGameDirectory { get; set; }

    [JsonIgnore]
    public bool HasValidClientId =>
        !string.IsNullOrWhiteSpace(MicrosoftClientId) &&
        !MicrosoftClientId.StartsWith("REPLACE_WITH", StringComparison.OrdinalIgnoreCase) &&
        Guid.TryParse(MicrosoftClientId, out _);

    public static BrandingConfig Load(string? baseDirectory = null)
    {
        var path = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "branding.json");
        if (!File.Exists(path)) return new BrandingConfig();
        try
        {
            return JsonSerializer.Deserialize<BrandingConfig>(File.ReadAllText(path)) ?? new BrandingConfig();
        }
        catch
        {
            return new BrandingConfig();
        }
    }
}
