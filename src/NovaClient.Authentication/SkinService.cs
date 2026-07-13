using NovaClient.Core.Http;
using NovaClient.Core.Logging;

namespace NovaClient.Authentication;

/// <summary>
/// Downloads the player's skin PNG from the URL returned by the official profile endpoint and
/// caches it locally. Head cropping (8×8 face + hat layer) happens in the UI layer.
/// </summary>
public sealed class SkinService
{
    private readonly string _cacheDirectory;

    public SkinService(string cacheDirectory)
    {
        _cacheDirectory = Path.Combine(cacheDirectory, "skins");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<string?> GetSkinFileAsync(MinecraftProfile profile, CancellationToken ct = default)
    {
        var cached = Path.Combine(_cacheDirectory, profile.Uuid + ".png");
        if (string.IsNullOrEmpty(profile.SkinUrl))
            return File.Exists(cached) ? cached : null;
        try
        {
            var bytes = await HttpProvider.Client.GetByteArrayAsync(profile.SkinUrl, ct);
            await File.WriteAllBytesAsync(cached, bytes, ct);
            return cached;
        }
        catch (Exception ex)
        {
            NovaLog.Warn("Skin", $"Skin download failed: {ex.Message}");
            return File.Exists(cached) ? cached : null;
        }
    }
}
