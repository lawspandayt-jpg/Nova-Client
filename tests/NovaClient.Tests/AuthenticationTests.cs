using System.Security.Cryptography;
using System.Text;
using NovaClient.Authentication;

namespace NovaClient.Tests;

public class PkceTests
{
    [Fact]
    public void CreatePkceSession_ChallengeIsS256OfVerifier()
    {
        var pkce = MicrosoftOAuth.CreatePkceSession();
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(pkce.CodeVerifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, pkce.CodeChallenge);
        Assert.True(pkce.CodeVerifier.Length is >= 43 and <= 128); // RFC 7636 length rules
    }

    [Fact]
    public void CreatePkceSession_ValuesAreUniquePerAttempt()
    {
        var first = MicrosoftOAuth.CreatePkceSession();
        var second = MicrosoftOAuth.CreatePkceSession();
        Assert.NotEqual(first.CodeVerifier, second.CodeVerifier);
        Assert.NotEqual(first.State, second.State);
    }

    [Fact]
    public void BuildAuthorizeUrl_ContainsRequiredOAuthParameters()
    {
        var oauth = new MicrosoftOAuth("11111111-2222-3333-4444-555555555555");
        var pkce = MicrosoftOAuth.CreatePkceSession();
        var url = oauth.BuildAuthorizeUrl(pkce, MicrosoftOAuth.NativeClientRedirect, "player@example.com");

        Assert.StartsWith("https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize?", url);
        Assert.Contains("client_id=11111111-2222-3333-4444-555555555555", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("code_challenge=" + pkce.CodeChallenge, url);
        Assert.Contains("login_hint=player%40example.com", url);
        Assert.Contains("scope=XboxLive.signin%20offline_access", url);
        Assert.DoesNotContain("client_secret", url); // public client — no secret anywhere
    }

    [Fact]
    public void ExtractCode_ReturnsCode_WhenStateMatches()
    {
        var pkce = MicrosoftOAuth.CreatePkceSession();
        var redirect = new Uri($"{MicrosoftOAuth.NativeClientRedirect}?code=M.C123_ABC&state={pkce.State}");
        Assert.Equal("M.C123_ABC", MicrosoftOAuth.ExtractCode(redirect, pkce));
    }

    [Fact]
    public void ExtractCode_Throws_OnStateMismatch()
    {
        var pkce = MicrosoftOAuth.CreatePkceSession();
        var redirect = new Uri($"{MicrosoftOAuth.NativeClientRedirect}?code=M.C123_ABC&state=forged-state");
        var ex = Assert.Throws<AuthException>(() => MicrosoftOAuth.ExtractCode(redirect, pkce));
        Assert.Equal(AuthErrorKind.StateMismatch, ex.Kind);
    }

    [Fact]
    public void ExtractCode_MapsUserCancellation()
    {
        var pkce = MicrosoftOAuth.CreatePkceSession();
        var redirect = new Uri($"{MicrosoftOAuth.NativeClientRedirect}?error=access_denied&error_description=User+cancelled");
        var ex = Assert.Throws<AuthException>(() => MicrosoftOAuth.ExtractCode(redirect, pkce));
        Assert.Equal(AuthErrorKind.UserCancelled, ex.Kind);
    }

    [Fact]
    public void AuthException_EveryKindHasAUserMessage()
    {
        foreach (AuthErrorKind kind in Enum.GetValues<AuthErrorKind>())
        {
            var message = new AuthException(kind, "internal").UserMessage;
            Assert.False(string.IsNullOrWhiteSpace(message));
        }
    }
}
