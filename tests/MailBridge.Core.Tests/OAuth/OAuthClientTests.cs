using System.Net;
using System.Web;
using MailBridge.Core.Security.OAuth;
using Xunit;

namespace MailBridge.Core.Tests.OAuth;

public class OAuthClientTests
{
    [Fact]
    public void Pkce_MatchesRfc7636Example()
    {
        // RFC 7636, Appendix B
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.CreateCodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Pkce_VerifierIsUrlSafeAndLongEnough()
    {
        var verifier = Pkce.CreateCodeVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
        Assert.NotEqual(verifier, Pkce.CreateCodeVerifier());
    }

    [Fact]
    public void BuildAuthorizationUri_ContainsCodeFlowWithPkceAndExtras()
    {
        var client = new OAuthClient(new HttpClient());

        var uri = client.BuildAuthorizationUri(TestOptions.Provider(), "http://localhost:5000", "st&ate", "challenge", " me@example.com ");

        Assert.StartsWith("https://login.example.test/authorize?", uri.AbsoluteUri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("test-client", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("http://localhost:5000", query["redirect_uri"]);
        Assert.Equal("imap offline_access", query["scope"]);
        Assert.Equal("st&ate", query["state"]);
        Assert.Equal("challenge", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("select_account", query["prompt"]);
        Assert.Equal("me@example.com", query["login_hint"]);
    }

    [Fact]
    public async Task RedeemCodeAsync_PostsCodeAndVerifier_AndReadsTokens()
    {
        var time = new ManualTimeProvider();
        var idToken = StubTokenEndpoint.IdToken("{\"preferred_username\":\"+491701234567\",\"email\":\"me@outlook.com\"}");
        var endpoint = new StubTokenEndpoint(_ => (HttpStatusCode.OK, StubTokenEndpoint.TokenJson("at-1", "rt-1", 3599, idToken)));
        var client = new OAuthClient(new HttpClient(endpoint), time);

        var tokens = await client.RedeemCodeAsync(TestOptions.Provider(), "the-code", "http://localhost:5000", "the-verifier");

        var form = Assert.Single(endpoint.Requests);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal("the-verifier", form["code_verifier"]);
        Assert.Equal("http://localhost:5000", form["redirect_uri"]);
        Assert.Equal("test-client", form["client_id"]);
        Assert.Equal("imap offline_access", form["scope"]);
        Assert.False(form.ContainsKey("client_secret"));

        Assert.Equal("at-1", tokens.AccessToken);
        Assert.Equal("rt-1", tokens.RefreshToken);
        Assert.Equal(time.Now.AddSeconds(3599), tokens.ExpiresAtUtc);
        Assert.Equal("me@outlook.com", tokens.Username); // email wins over a phone-number preferred_username
        Assert.DoesNotContain("at-1", tokens.ToString());
    }

    [Fact]
    public async Task TokenRequests_OfSecretProvider_SendSecretButNoScope()
    {
        var endpoint = new StubTokenEndpoint(_ => (HttpStatusCode.OK, StubTokenEndpoint.TokenJson("at", "rt")));
        var client = new OAuthClient(new HttpClient(endpoint));

        await client.RedeemCodeAsync(TestOptions.SecretProvider(), "code", "http://localhost:5000", "verifier");
        await client.RefreshAsync(TestOptions.SecretProvider(), "rt");

        Assert.Equal(2, endpoint.Requests.Count);
        Assert.All(endpoint.Requests, form =>
        {
            Assert.Equal("secret-client", form["client_id"]);
            Assert.Equal("test-secret", form["client_secret"]);
            Assert.False(form.ContainsKey("scope"));
        });
    }

    [Fact]
    public void Providers_AreConfiguredOnlyWithEverythingTheyRequire()
    {
        Assert.True(MicrosoftOAuth.CreateOptions("ms-client").IsConfigured);
        Assert.False(MicrosoftOAuth.CreateOptions(" ").IsConfigured);
        Assert.True(GoogleOAuth.CreateOptions("g-client", "g-secret").IsConfigured);
        Assert.False(GoogleOAuth.CreateOptions("g-client", "").IsConfigured); // Google rejects desktop clients without secret
        Assert.False(GoogleOAuth.CreateOptions("", "g-secret").IsConfigured);
    }

    [Fact]
    public void GoogleAuthorizationUri_RequestsOfflineAccessWithConsent()
    {
        var uri = new OAuthClient(new HttpClient()).BuildAuthorizationUri(
            GoogleOAuth.CreateOptions("g-client", "g-secret"), "http://localhost:5000", "state", "challenge");

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", uri.AbsoluteUri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent select_account", query["prompt"]);
        Assert.Equal("https://mail.google.com/ openid email", query["scope"]);
        Assert.Null(query["client_secret"]); // the secret only goes to the token endpoint
    }

    [Theory]
    [InlineData("{\"preferred_username\":\"user@contoso.com\"}", "user@contoso.com")]
    [InlineData("{\"upn\":\"user@contoso.com\"}", "user@contoso.com")]
    [InlineData("{\"preferred_username\":\"+491701234567\"}", null)]
    public void ReadUsernameFromIdToken_UsesFirstEmailLikeClaim(string payload, string? expected)
    {
        Assert.Equal(expected, OAuthClient.ReadUsernameFromIdToken(StubTokenEndpoint.IdToken(payload)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.!!!.c")]
    public void ReadUsernameFromIdToken_ReturnsNullForMalformedTokens(string? idToken)
    {
        Assert.Null(OAuthClient.ReadUsernameFromIdToken(idToken));
    }

    [Fact]
    public async Task RefreshAsync_InvalidGrant_RequiresSignIn()
    {
        var endpoint = new StubTokenEndpoint(_ => (HttpStatusCode.BadRequest,
            "{\"error\":\"invalid_grant\",\"error_description\":\"AADSTS70008: The refresh token has expired.\"}"));
        var client = new OAuthClient(new HttpClient(endpoint));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => client.RefreshAsync(TestOptions.Provider(), "old-rt"));

        Assert.Equal("refresh_token", endpoint.Requests[0]["grant_type"]);
        Assert.Equal("old-rt", endpoint.Requests[0]["refresh_token"]);
        Assert.Equal("invalid_grant", ex.Error);
        Assert.True(ex.RequiresSignIn);
        Assert.Contains("AADSTS70008", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "<html>Service unavailable</html>")]
    [InlineData(HttpStatusCode.OK, "{\"token_type\":\"Bearer\"}")]
    [InlineData(HttpStatusCode.OK, "[]")]
    public async Task RefreshAsync_UnexpectedResponse_IsNotASignInProblem(HttpStatusCode status, string body)
    {
        var client = new OAuthClient(new HttpClient(new StubTokenEndpoint(_ => (status, body))));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => client.RefreshAsync(TestOptions.Provider(), "rt"));

        Assert.Equal("invalid_response", ex.Error);
        Assert.False(ex.RequiresSignIn);
    }

    [Fact]
    public async Task RefreshAsync_WithoutNewRefreshToken_ReturnsNullRefreshToken()
    {
        var client = new OAuthClient(new HttpClient(new StubTokenEndpoint(_ => (HttpStatusCode.OK, StubTokenEndpoint.TokenJson("at-2")))));

        var tokens = await client.RefreshAsync(TestOptions.Provider(), "rt");

        Assert.Equal("at-2", tokens.AccessToken);
        Assert.Null(tokens.RefreshToken);
        Assert.Null(tokens.Username);
    }
}
