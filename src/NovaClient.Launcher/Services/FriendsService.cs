using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NovaClient.Core.Http;
using NovaClient.Core.Logging;

namespace NovaClient.Launcher.Services;

public sealed class Friend
{
    [JsonPropertyName("uuid")] public string Uuid { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("skinUrl")] public string? SkinUrl { get; set; }
}

/// <summary>
/// Local friends list. Friends are added by Minecraft username, verified against Mojang's public
/// profile API (so only real accounts can be added), and persisted to config/friends.json.
/// Online presence is layered on separately by <see cref="PresenceService"/> when configured.
/// </summary>
public sealed class FriendsService
{
    private const string NameToUuid = "https://api.mojang.com/users/profiles/minecraft/";
    private const string Profile = "https://sessionserver.mojang.com/session/minecraft/profile/";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    public List<Friend> Friends { get; private set; } = new();

    public FriendsService(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        _filePath = Path.Combine(configDirectory, "friends.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
                Friends = JsonSerializer.Deserialize<List<Friend>>(File.ReadAllText(_filePath)) ?? new();
        }
        catch (Exception ex) { NovaLog.Warn("Friends", $"Could not load friends: {ex.Message}"); }
    }

    private void Save() => File.WriteAllText(_filePath, JsonSerializer.Serialize(Friends, JsonOptions));

    public bool Contains(string uuid) => Friends.Any(f => f.Uuid == uuid);

    /// <summary>Looks up a username on Mojang and adds it. Throws with a readable reason on failure.</summary>
    public async Task<Friend> AddByNameAsync(string username, CancellationToken ct = default)
    {
        username = username.Trim();
        if (username.Length is < 3 or > 16 || username.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("Enter a valid Minecraft username (3–16 letters, numbers, underscores).");

        var response = await HttpProvider.Client.GetAsync(NameToUuid + Uri.EscapeDataString(username), ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound || (int)response.StatusCode == 204)
            throw new InvalidOperationException($"No Minecraft account named \"{username}\" exists.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Could not reach Mojang to look up that username. Try again.");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var uuid = doc.RootElement.GetProperty("id").GetString()!;
        var name = doc.RootElement.GetProperty("name").GetString()!;

        if (Contains(uuid)) throw new InvalidOperationException($"{name} is already in your friends list.");

        var skinUrl = await TryGetSkinUrlAsync(uuid, ct);
        var friend = new Friend { Uuid = uuid, Name = name, SkinUrl = skinUrl };
        Friends.Add(friend);
        Save();
        NovaLog.Info("Friends", $"Added friend {name}.");
        return friend;
    }

    public void Remove(string uuid)
    {
        Friends.RemoveAll(f => f.Uuid == uuid);
        Save();
    }

    private static async Task<string?> TryGetSkinUrlAsync(string uuid, CancellationToken ct)
    {
        try
        {
            var json = await HttpProvider.Client.GetStringAsync(Profile + uuid, ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.GetProperty("properties").EnumerateArray())
            {
                if (prop.GetProperty("name").GetString() != "textures") continue;
                var decoded = Convert.FromBase64String(prop.GetProperty("value").GetString()!);
                using var tex = JsonDocument.Parse(decoded);
                if (tex.RootElement.GetProperty("textures").TryGetProperty("SKIN", out var skin))
                    return skin.GetProperty("url").GetString();
            }
        }
        catch (Exception ex) { NovaLog.Debug("Friends", $"Skin lookup failed: {ex.Message}"); }
        return null;
    }
}
