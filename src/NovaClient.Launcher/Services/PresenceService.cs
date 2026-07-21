using System.Net.Http;
using System.Text;
using System.Text.Json;
using NovaClient.Core.Http;
using NovaClient.Core.Logging;

namespace NovaClient.Launcher.Services;

/// <summary>
/// Optional online-presence layer. When a presence endpoint is configured (branding.json →
/// presenceApiUrl, e.g. a free Cloudflare Worker), the launcher sends a heartbeat for the signed-in
/// player and can look up which friends are currently online. With no endpoint configured it is
/// completely inert — the friends list still works, just without live online status (no faking).
/// </summary>
public sealed class PresenceService
{
    private readonly string? _endpoint;

    public bool Enabled => !string.IsNullOrWhiteSpace(_endpoint)
                           && (_endpoint!.StartsWith("https://") );

    public PresenceService(string? presenceApiUrl) => _endpoint = presenceApiUrl?.Trim();

    /// <summary>Announce that this player is online (call periodically while the launcher runs).</summary>
    public async Task HeartbeatAsync(string uuid, string name, CancellationToken ct = default)
    {
        if (!Enabled) return;
        try
        {
            var body = JsonSerializer.Serialize(new { uuid, name });
            await HttpProvider.Client.PostAsync($"{_endpoint}/heartbeat",
                new StringContent(body, Encoding.UTF8, "application/json"), ct);
        }
        catch (Exception ex) { NovaLog.Debug("Presence", $"Heartbeat failed: {ex.Message}"); }
    }

    /// <summary>Returns the set of friend UUIDs currently online (empty when unavailable).</summary>
    public async Task<HashSet<string>> GetOnlineAsync(IEnumerable<string> friendUuids, CancellationToken ct = default)
    {
        var result = new HashSet<string>();
        if (!Enabled) return result;
        try
        {
            var body = JsonSerializer.Serialize(new { uuids = friendUuids.ToArray() });
            var response = await HttpProvider.Client.PostAsync($"{_endpoint}/status",
                new StringContent(body, Encoding.UTF8, "application/json"), ct);
            if (!response.IsSuccessStatusCode) return result;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("online", out var online))
                foreach (var el in online.EnumerateArray())
                    if (el.GetString() is { } uuid) result.Add(uuid);
        }
        catch (Exception ex) { NovaLog.Debug("Presence", $"Status lookup failed: {ex.Message}"); }
        return result;
    }
}
