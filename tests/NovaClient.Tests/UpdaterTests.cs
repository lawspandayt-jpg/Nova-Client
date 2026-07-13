using NovaClient.Updater;

namespace NovaClient.Tests;

public class UpdaterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nova-tests-" + Guid.NewGuid().ToString("N"));

    public UpdaterTests()
    {
        Directory.CreateDirectory(_dir);
    }

    private async Task<string> WriteManifestAsync(string launcherVersion)
    {
        var payload = Path.Combine(_dir, "payload.txt");
        await File.WriteAllTextAsync(payload, "new file content");
        var manifestPath = Path.Combine(_dir, "manifest.json");
        await UpdateService.WriteDevManifestAsync(manifestPath, launcherVersion, launcherVersion,
            new[] { ("payload.txt", new Uri(payload).AbsoluteUri, payload) });
        return manifestPath;
    }

    [Fact]
    public async Task Check_DetectsNewerVersion()
    {
        var manifest = await WriteManifestAsync("2.0.0");
        var service = new UpdateService(new Uri(manifest).AbsoluteUri, _dir, _dir);
        var result = await service.CheckAsync("1.0.0");
        Assert.True(result.UpdateAvailable);
        Assert.Equal("2.0.0", result.LatestVersion);
    }

    [Fact]
    public async Task Check_ReportsUpToDate_ForSameOrOlder()
    {
        var manifest = await WriteManifestAsync("1.0.0");
        var service = new UpdateService(new Uri(manifest).AbsoluteUri, _dir, _dir);
        Assert.False((await service.CheckAsync("1.0.0")).UpdateAvailable);
        Assert.False((await service.CheckAsync("1.2.0")).UpdateAvailable);
    }

    [Fact]
    public async Task Check_RejectsPlainHttpManifest()
    {
        var service = new UpdateService("http://insecure.example.com/manifest.json", _dir, _dir);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckAsync("1.0.0"));
    }

    [Fact]
    public async Task Apply_RejectsPathTraversal()
    {
        var manifest = new UpdateManifest
        {
            LauncherVersion = "2.0.0",
            Files = { new UpdateFile { Path = "..\\..\\evil.exe", Url = "https://example.com/x", Sha256 = "00", Size = 1 } }
        };
        var service = new UpdateService("https://example.com/manifest.json", _dir, _dir);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAndApplyAsync(manifest, null));
    }

    [Fact]
    public async Task DevManifest_ContainsRealSha256()
    {
        var manifestPath = await WriteManifestAsync("1.1.0");
        var text = await File.ReadAllTextAsync(manifestPath);
        var manifest = System.Text.Json.JsonSerializer.Deserialize<UpdateManifest>(text)!;
        Assert.Single(manifest.Files);
        Assert.Equal(64, manifest.Files[0].Sha256.Length);
        Assert.True(manifest.Files[0].Size > 0);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
