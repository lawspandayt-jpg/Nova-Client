using System.Net.Http;
using System.Text;
using System.Text.Json;
using NovaClient.Core.Http;
using NovaClient.Core.Logging;

namespace NovaClient.Launcher.Services;

public sealed record BackendFriend(string Uuid, string Name);
public sealed record IncomingRequest(string FromUuid, string FromName);
public sealed record RequestResult(string Status, string Message);

/// <summary>
/// Client for the Nova friends + presence backend (a free Cloudflare Worker, URL in branding.json
/// → presenceApiUrl). Handles heartbeat, online status/count, and the full friend-request flow
/// (send → pending → accept). When no endpoint is configured it is inert and every call is a no-op
/// so the launcher still runs — nothing is faked.
/// </summary>
public sealed class PresenceService
{
    private readonly string? _endpoint;

    public bool Enabled => !string.IsNullOrWhiteSpace(_endpoint) && _endpoint!.StartsWith("https://");

    public PresenceService(string? presenceApiUrl) => _endpoint = presenceApiUrl?.Trim().TrimEnd('/');

    private async Task<JsonDocument?> PostAsync(string path, object body, CancellationToken ct)
    {
        if (!Enabled) return null;
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await HttpProvider.Client.PostAsync($"{_endpoint}{path}", content, ct);
            if (!response.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            NovaLog.Debug("Presence", $"{path} failed: {ex.Message}");
            return null;
        }
    }

    public async Task HeartbeatAsync(string uuid, string name, CancellationToken ct = default)
        => (await PostAsync("/heartbeat", new { uuid, name }, ct))?.Dispose();

    public async Task<int> GetOnlineCountAsync(CancellationToken ct = default)
    {
        using var doc = await PostAsync("/count", new { }, ct);
        return doc?.RootElement.TryGetProperty("count", out var c) == true ? c.GetInt32() : 0;
    }

    public async Task<HashSet<string>> GetOnlineAsync(IEnumerable<string> uuids, CancellationToken ct = default)
    {
        var result = new HashSet<string>();
        using var doc = await PostAsync("/status", new { uuids = uuids.ToArray() }, ct);
        if (doc?.RootElement.TryGetProperty("online", out var online) == true)
            foreach (var el in online.EnumerateArray())
                if (el.GetString() is { } u) result.Add(u);
        return result;
    }

    /// <summary>Sends a friend request by username. Returns the backend status: pending / accepted / not_found / already_friends.</summary>
    public async Task<RequestResult> SendRequestAsync(string fromUuid, string fromName, string toName, CancellationToken ct = default)
    {
        using var doc = await PostAsync("/request", new { fromUuid, fromName, toName }, ct);
        if (doc is null) return new RequestResult("error", "Could not reach the friends server.");
        var root = doc.RootElement;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "error" : "error";
        var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        return new RequestResult(status, message);
    }

    public async Task<List<IncomingRequest>> GetInboxAsync(string uuid, CancellationToken ct = default)
    {
        var list = new List<IncomingRequest>();
        using var doc = await PostAsync("/inbox", new { uuid }, ct);
        if (doc?.RootElement.TryGetProperty("requests", out var reqs) == true)
            foreach (var r in reqs.EnumerateArray())
                list.Add(new IncomingRequest(r.GetProperty("fromUuid").GetString()!, r.GetProperty("fromName").GetString()!));
        return list;
    }

    public async Task RespondAsync(string uuid, string fromUuid, bool accept, CancellationToken ct = default)
        => (await PostAsync("/respond", new { uuid, fromUuid, accept }, ct))?.Dispose();

    public async Task<List<BackendFriend>> GetFriendsAsync(string uuid, CancellationToken ct = default)
    {
        var list = new List<BackendFriend>();
        using var doc = await PostAsync("/friends", new { uuid }, ct);
        if (doc?.RootElement.TryGetProperty("friends", out var fr) == true)
            foreach (var f in fr.EnumerateArray())
                list.Add(new BackendFriend(f.GetProperty("uuid").GetString()!, f.GetProperty("name").GetString() ?? ""));
        return list;
    }

    public async Task<List<BackendFriend>> GetOutboxAsync(string uuid, CancellationToken ct = default)
    {
        var list = new List<BackendFriend>();
        using var doc = await PostAsync("/outbox", new { uuid }, ct);
        if (doc?.RootElement.TryGetProperty("pending", out var p) == true)
            foreach (var f in p.EnumerateArray())
                list.Add(new BackendFriend(f.GetProperty("toUuid").GetString()!, f.GetProperty("toName").GetString() ?? ""));
        return list;
    }
}
