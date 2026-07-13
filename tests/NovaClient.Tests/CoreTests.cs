using NovaClient.Core;
using NovaClient.Core.Security;
using NovaClient.Core.Settings;
using NovaClient.Core.Util;

namespace NovaClient.Tests;

public class EmailMaskerTests
{
    [Theory]
    [InlineData("oliver@outlook.com", "o****r@outlook.com")]
    [InlineData("ab@x.com", "a*@x.com")]
    [InlineData("a@x.com", "*@x.com")]
    [InlineData("", "")]
    public void Mask_ProducesExpectedShape(string input, string expected)
    {
        Assert.Equal(expected, EmailMasker.Mask(input));
    }

    [Theory]
    [InlineData("name@example.com", true)]
    [InlineData("first.last@sub.domain.co.uk", true)]
    [InlineData("no-at-sign", false)]
    [InlineData("two@@example.com", false)]
    [InlineData("trailing@", false)]
    [InlineData("@leading.com", false)]
    [InlineData("space in@mail.com", false)]
    [InlineData("nodot@examplecom", false)]
    [InlineData(null, false)]
    public void LooksValid_ChecksBasicShape(string? email, bool expected)
    {
        Assert.Equal(expected, EmailMasker.LooksValid(email));
    }
}

public class RamValidatorTests
{
    [Theory]
    [InlineData(512, 16384, 1024)]    // below minimum → clamped up
    [InlineData(2048, 16384, 2048)]   // in range → unchanged
    [InlineData(64000, 16384, 14336)] // above (total - 2GB reserve) → clamped down
    [InlineData(8192, 4096, 2048)]    // small system → max(min, total-reserve)
    public void ClampAgainst_EnforcesSafeBounds(int requested, int total, int expected)
    {
        Assert.Equal(expected, RamValidator.ClampAgainst(requested, total));
    }

    [Fact]
    public void Maximum_NeverExceedsPhysicalMinusReserve()
    {
        Assert.True(RamValidator.MaximumMb <= RamValidator.TotalPhysicalMb - 2048
                    || RamValidator.MaximumMb == RamValidator.MinimumMb);
    }
}

public class SemVersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.2.0", "1.1.9", 1)]
    [InlineData("2.0.0", "2.0.0", 0)]
    [InlineData("1.0.0-beta", "1.0.0", -1)] // pre-release sorts below release
    [InlineData("v1.3.0", "1.2.9", 1)]
    public void CompareTo_OrdersCorrectly(string left, string right, int expectedSign)
    {
        Assert.True(SemVersion.TryParse(left, out var a));
        Assert.True(SemVersion.TryParse(right, out var b));
        Assert.Equal(expectedSign, Math.Sign(a.CompareTo(b)));
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.x.3")]
    public void TryParse_RejectsGarbage(string input)
    {
        Assert.False(SemVersion.TryParse(input, out _));
    }
}

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nova-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var service = new SettingsService(_dir);
        service.Current.RememberEmail = true;
        service.Current.RememberedEmail = "player@example.com";
        service.Current.RamMb = 3072;
        service.Current.AfterLaunch = AfterLaunchBehavior.Minimize;
        service.Save();

        var reloaded = new SettingsService(_dir);
        Assert.True(reloaded.Current.RememberEmail);
        Assert.Equal("player@example.com", reloaded.Current.RememberedEmail);
        Assert.Equal(3072, reloaded.Current.RamMb);
        Assert.Equal(AfterLaunchBehavior.Minimize, reloaded.Current.AfterLaunch);
    }

    [Fact]
    public void Load_SurvivesCorruptedFile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "launcher-settings.json"), "{not valid json!!");
        var service = new SettingsService(_dir);
        Assert.True(service.Current.RamMb >= RamValidator.MinimumMb);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

public class SecureTokenStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nova-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveLoadDelete_RoundTrips()
    {
        var store = new SecureTokenStore(_dir);
        store.Save("auth", "{\"secret\":\"value\"}");

        // Encrypted at rest: the plaintext must not appear in the blob.
        var blob = File.ReadAllBytes(Path.Combine(_dir, "auth.bin"));
        Assert.DoesNotContain("secret", System.Text.Encoding.UTF8.GetString(blob));

        Assert.Equal("{\"secret\":\"value\"}", store.Load("auth"));
        store.Delete("auth");
        Assert.Null(store.Load("auth"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

public class NovaPathsTests
{
    [Fact]
    public void EnsureCreated_CreatesAllRequiredFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "nova-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new NovaPaths(root);
            paths.EnsureCreated();
            foreach (var dir in new[]
                     {
                         paths.Assets, paths.Libraries, paths.Versions, paths.Natives, paths.Logs,
                         paths.Screenshots, paths.ResourcePacks, paths.Config, paths.Cache,
                         paths.JavaRuntime, paths.ClientFiles, paths.CrashReports
                     })
            {
                Assert.True(Directory.Exists(dir), dir);
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
