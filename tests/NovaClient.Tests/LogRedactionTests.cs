using NovaClient.Core.Logging;

namespace NovaClient.Tests;

/// <summary>Proves sensitive material never survives into log output.</summary>
public class LogRedactionTests
{
    [Theory]
    [InlineData("\"access_token\":\"EwAoA1234567890abcdefghijklmnopqrstuvwxyzEwAoA1234567890abcdefg\"")]
    [InlineData("\"refresh_token\":\"M.R3_BAY.abcdefghijklmnop-qrstuvwxyz0123456789\"")]
    [InlineData("Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U")]
    [InlineData("identityToken = XBL3.0 x=1234567890;eyJhbGciOiJSUzI1NiJ9.payload.signature")]
    [InlineData("code=M.C507_BAY.2.U.abc-def-ghi&state=xyz")]
    [InlineData("code_verifier=dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")]
    [InlineData("device_code=DAQABAAEAAAD--DLA3VO7QrddgJg7Wevr")]
    [InlineData("Set-Cookie: ESTSAUTHPERSISTENT=1234567890abcdef")]
    public void Redact_RemovesSecrets(string input)
    {
        var output = LogRedactor.Redact(input);

        Assert.DoesNotContain("EwAoA1234567890", output);
        Assert.DoesNotContain("M.R3_BAY", output);
        Assert.DoesNotContain("eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0", output);
        Assert.DoesNotContain("x=1234567890;", output);
        Assert.DoesNotContain("M.C507_BAY", output);
        Assert.DoesNotContain("dBjftJeZ4CVP", output);
        Assert.DoesNotContain("DAQABAAEAAAD", output);
        Assert.DoesNotContain("ESTSAUTHPERSISTENT=1234567890", output);
        Assert.Contains("[REDACTED", output);
    }

    [Fact]
    public void Redact_KeepsHarmlessText()
    {
        const string text = "Downloaded 42 files in 3.1s for user Steve (uuid 069a79f4-44e9-4726-a5be-fca90e38aaf5)";
        Assert.Equal(text, LogRedactor.Redact(text));
    }

    [Fact]
    public void Redact_HandlesNullAndEmpty()
    {
        Assert.Equal(string.Empty, LogRedactor.Redact(null));
        Assert.Equal(string.Empty, LogRedactor.Redact(""));
    }

    [Fact]
    public void Redact_JsonTokenResponse_FullyScrubbed()
    {
        const string json = "{\"token_type\":\"bearer\",\"expires_in\":3600," +
                            "\"access_token\":\"EwAoA8l6BAAU1234567890abcdefghijklmnopqrstuvwxyz1234567890\"," +
                            "\"refresh_token\":\"M.R3_BAY.CYxAbCdEfGhIjKlMnOpQrStUvWxYz012345678\"}";
        var output = LogRedactor.Redact(json);
        Assert.DoesNotContain("EwAoA8l6BAAU", output);
        Assert.DoesNotContain("M.R3_BAY.CYx", output);
        Assert.Contains("expires_in", output); // non-secrets survive
    }
}
