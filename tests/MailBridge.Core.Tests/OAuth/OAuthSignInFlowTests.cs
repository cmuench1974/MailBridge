using System.Net;
using System.Web;
using MailBridge.Core.Security.OAuth;
using Xunit;

namespace MailBridge.Core.Tests.OAuth;

/// <summary>
/// Runs the real loopback listener: the "browser" is an HttpClient that follows the
/// authorization URL's redirect_uri the way the provider would redirect back.
/// </summary>
public class OAuthSignInFlowTests
{
    private static readonly HttpClient Browser = new() { Timeout = TimeSpan.FromSeconds(10) };

    [Fact]
    public async Task SignInAsync_RedeemsCodeFromRedirect_WithMatchingVerifier()
    {
        string? challenge = null;
        string? redirectUri = null;
        string? pageBody = null;
        var endpoint = new StubTokenEndpoint(_ => (HttpStatusCode.OK, StubTokenEndpoint.TokenJson("at", "rt")));
        var flow = new OAuthSignInFlow(new OAuthClient(new HttpClient(endpoint)));

        var tokens = await flow.SignInAsync(TestOptions.Provider(), async uri =>
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            challenge = query["code_challenge"];
            redirectUri = query["redirect_uri"];
            Assert.Equal("me@example.com", query["login_hint"]);

            // Browsers often ask for a favicon first; it must not end the sign-in.
            Assert.Equal(HttpStatusCode.NotFound, (await Browser.GetAsync(redirectUri + "/favicon.ico")).StatusCode);
            pageBody = await Browser.GetStringAsync($"{redirectUri}/?code=auth-code&state={Uri.EscapeDataString(query["state"]!)}");
        }, TestOptions.Page, "me@example.com", CancellationToken.None);

        Assert.Equal("at", tokens.AccessToken);
        Assert.Matches(@"^http://localhost:\d+$", redirectUri);
        Assert.Contains("Signed in.", pageBody);

        var form = Assert.Single(endpoint.Requests);
        Assert.Equal("auth-code", form["code"]);
        Assert.Equal(redirectUri, form["redirect_uri"]);
        Assert.Equal(challenge, Pkce.CreateCodeChallenge(form["code_verifier"]));
    }

    [Fact]
    public async Task SignInAsync_StateMismatch_IsRejectedWithoutRedeemingTheCode()
    {
        var endpoint = new StubTokenEndpoint(_ => (HttpStatusCode.OK, StubTokenEndpoint.TokenJson("at", "rt")));
        var flow = new OAuthSignInFlow(new OAuthClient(new HttpClient(endpoint)));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => flow.SignInAsync(TestOptions.Provider(), async uri =>
        {
            var redirectUri = HttpUtility.ParseQueryString(uri.Query)["redirect_uri"];
            await Browser.GetStringAsync($"{redirectUri}/?code=injected&state=forged");
        }, TestOptions.Page, null, CancellationToken.None));

        Assert.Equal("invalid_state", ex.Error);
        Assert.Empty(endpoint.Requests);
    }

    [Fact]
    public async Task SignInAsync_UserDeclines_ThrowsProviderErrorAndShowsFailurePage()
    {
        string? pageBody = null;
        var flow = new OAuthSignInFlow(new OAuthClient(new HttpClient(new StubTokenEndpoint(_ => (HttpStatusCode.OK, "{}")))));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => flow.SignInAsync(TestOptions.Provider(), async uri =>
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            pageBody = await Browser.GetStringAsync(
                $"{query["redirect_uri"]}/?error=access_denied&error_description=The+user+declined&state={Uri.EscapeDataString(query["state"]!)}");
        }, TestOptions.Page, null, CancellationToken.None));

        Assert.Equal("access_denied", ex.Error);
        Assert.Contains("The user declined", ex.Message);
        Assert.Contains("Sign-in failed.", pageBody);
    }

    [Fact]
    public async Task SignInAsync_Cancelled_StopsWaiting()
    {
        var flow = new OAuthSignInFlow(new OAuthClient(new HttpClient()));
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flow.SignInAsync(TestOptions.Provider(), _ =>
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(200)); // the user never completes the login
            return Task.CompletedTask;
        }, TestOptions.Page, null, cts.Token));
    }

    [Fact]
    public async Task SignInAsync_BrowserCannotBeOpened_FailsImmediately()
    {
        var flow = new OAuthSignInFlow(new OAuthClient(new HttpClient()));

        var signIn = flow.SignInAsync(TestOptions.Provider(), _ => throw new InvalidOperationException("no browser"),
            TestOptions.Page, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => signIn.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("no browser", ex.Message);
    }

    [Fact]
    public async Task SignInAsync_WithoutClientId_Throws()
    {
        var flow = new OAuthSignInFlow(new OAuthClient(new HttpClient()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.SignInAsync(
            TestOptions.Provider(clientId: " "), _ => Task.CompletedTask, TestOptions.Page, null, CancellationToken.None));
    }
}
