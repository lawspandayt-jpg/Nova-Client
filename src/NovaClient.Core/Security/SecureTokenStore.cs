using System.Security.Cryptography;
using System.Text;
using NovaClient.Core.Logging;

namespace NovaClient.Core.Security;

/// <summary>
/// Encrypts secrets at rest with the Windows Data Protection API (per-user scope). Tokens are
/// never written to disk in plain text and can only be decrypted by the same Windows account.
/// </summary>
public sealed class SecureTokenStore
{
    // Additional entropy binds the blobs to this application (defense in depth on top of DPAPI).
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NovaClient.SecureTokenStore.v1");

    private readonly string _directory;

    public SecureTokenStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    private string PathFor(string key) => Path.Combine(_directory, key + ".bin");

    public void Save(string key, string plaintext)
    {
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(key), data);
    }

    public string? Load(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;
        try
        {
            var data = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (CryptographicException ex)
        {
            NovaLog.Warn("SecureStore", $"Could not decrypt '{key}' (different Windows user or corrupted blob): {ex.Message}");
            return null;
        }
    }

    public void Delete(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
    }
}
