using System.Net;
using MailBridge.Core.Models;
using MailBridge.Core.Security.OAuth;
using MailBridge.Core.Services;
using Xunit;

namespace MailBridge.Core.Tests.OAuth;

public class AccountCredentialProviderTests
{
    private readonly InMemoryCredentialStore _store = new();
    private readonly ManualTimeProvider _time = new();

    private static EmailAccount Account(AccountAuthMethod method) => new()
    {
        Host = MicrosoftOAuth.ImapHost,
        Username = "me@outlook.com",
        CredentialKey = "MailBridge:outlook.office365.com:me@outlook.com",
        AuthMethod = method,
    };

    private AccountCredentialProvider Provider(StubTokenEndpoint endpoint, string clientId = "test-client") =>
        new(_store, new OAuthClient(new HttpClient(endpoint), _time), TestOptions.Provider(clientId), _time);

    private static StubTokenEndpoint RotatingEndpoint()
    {
        var counter = 0;
        return new StubTokenEndpoint(_ =>
        {
            var n = Interlocked.Increment(ref counter);
            return (HttpStatusCode.OK, StubTokenEndpoint.TokenJson($"at-{n}", $"rt-{n}", expiresIn: 3600));
        });
    }

    [Fact]
    public async Task PasswordAccount_ReturnsStoredPassword()
    {
        var account = Account(AccountAuthMethod.Password);
        _store.Save(account.CredentialKey, account.Username, "secret");

        var credential = await Provider(RotatingEndpoint()).GetAsync(account);

        Assert.False(credential.IsOAuth2);
        Assert.Equal("secret", credential.Secret);
        Assert.DoesNotContain("secret", credential.ToString());
    }

    [Fact]
    public async Task PasswordAccount_WithoutPassword_ReportsMissingPassword()
    {
        var ex = await Assert.ThrowsAsync<CredentialUnavailableException>(() =>
            Provider(RotatingEndpoint()).GetAsync(Account(AccountAuthMethod.Password)));

        Assert.Equal(CredentialUnavailableReason.MissingPassword, ex.Reason);
    }

    [Fact]
    public async Task OAuthAccount_RefreshesOnce_CachesAccessToken_AndStoresRotatedRefreshToken()
    {
        var account = Account(AccountAuthMethod.MicrosoftOAuth2);
        _store.Save(account.CredentialKey, account.Username, "rt-0");
        var endpoint = RotatingEndpoint();
        var provider = Provider(endpoint);

        var first = await provider.GetAsync(account);
        _time.Now = _time.Now.AddMinutes(30);
        var second = await provider.GetAsync(account);

        Assert.True(first.IsOAuth2);
        Assert.Equal("at-1", first.Secret);
        Assert.Equal("at-1", second.Secret);
        Assert.Equal("rt-0", Assert.Single(endpoint.Requests)["refresh_token"]);
        Assert.Equal("rt-1", _store.TryGet(account.CredentialKey, account.Username));
    }

    [Fact]
    public async Task OAuthAccount_RefreshesAgainShortlyBeforeExpiry()
    {
        var account = Account(AccountAuthMethod.MicrosoftOAuth2);
        _store.Save(account.CredentialKey, account.Username, "rt-0");
        var endpoint = RotatingEndpoint();
        var provider = Provider(endpoint);

        await provider.GetAsync(account);
        _time.Now = _time.Now.AddMinutes(56); // inside the 5-minute safety margin of the 60-minute token
        var renewed = await provider.GetAsync(account);

        Assert.Equal("at-2", renewed.Secret);
        Assert.Equal(new[] { "rt-0", "rt-1" }, endpoint.Requests.Select(r => r["refresh_token"]));
        Assert.Equal("rt-2", _store.TryGet(account.CredentialKey, account.Username));
    }

    [Fact]
    public async Task OAuthAccount_ParallelRequests_ShareOneRefresh()
    {
        var account = Account(AccountAuthMethod.MicrosoftOAuth2);
        _store.Save(account.CredentialKey, account.Username, "rt-0");
        var endpoint = RotatingEndpoint();
        var provider = Provider(endpoint);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => provider.GetAsync(account))));

        Assert.All(results, r => Assert.Equal("at-1", r.Secret));
        Assert.Single(endpoint.Requests);
    }

    [Fact]
    public async Task OAuthAccount_RefreshWithoutRotation_KeepsStoredRefreshToken()
    {
        var account = Account(AccountAuthMethod.MicrosoftOAuth2);
        _store.Save(account.CredentialKey, account.Username, "rt-0");
        var provider = Provider(new StubTokenEndpoint(_ => (HttpStatusCode.OK, StubTokenEndpoint.TokenJson("at"))));

        await provider.GetAsync(account);

        Assert.Equal("rt-0", _store.TryGet(account.CredentialKey, account.Username));
    }

    [Fact]
    public async Task OAuthAccount_RevokedRefreshToken_RequiresSignIn()
    {
        var account = Account(AccountAuthMethod.MicrosoftOAuth2);
        _store.Save(account.CredentialKey, account.Username, "rt-0");
        var provider = Provider(new StubTokenEndpoint(_ => (HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}")));

        var ex = await Assert.ThrowsAsync<CredentialUnavailableException>(() => provider.GetAsync(account));

        Assert.Equal(CredentialUnavailableReason.SignInRequired, ex.Reason);
        Assert.IsType<OAuthException>(ex.InnerException);
    }

    [Fact]
    public async Task OAuthAccount_ServerError_IsNotReportedAsSignInProblem()
    {
        var account = Account(AccountAuthMethod.MicrosoftOAuth2);
        _store.Save(account.CredentialKey, account.Username, "rt-0");
        var provider = Provider(new StubTokenEndpoint(_ => (HttpStatusCode.ServiceUnavailable, "busy")));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => provider.GetAsync(account));

        Assert.False(ex.RequiresSignIn);
        Assert.Equal("rt-0", _store.TryGet(account.CredentialKey, account.Username));
    }

    [Fact]
    public async Task OAuthAccount_WithoutStoredSignIn_RequiresSignIn()
    {
        var endpoint = RotatingEndpoint();

        var ex = await Assert.ThrowsAsync<CredentialUnavailableException>(() =>
            Provider(endpoint).GetAsync(Account(AccountAuthMethod.MicrosoftOAuth2)));

        Assert.Equal(CredentialUnavailableReason.SignInRequired, ex.Reason);
        Assert.Empty(endpoint.Requests);
    }

    [Fact]
    public async Task OAuthAccount_WithoutClientId_ReportsNotConfigured()
    {
        var ex = await Assert.ThrowsAsync<CredentialUnavailableException>(() =>
            Provider(RotatingEndpoint(), clientId: "").GetAsync(Account(AccountAuthMethod.MicrosoftOAuth2)));

        Assert.Equal(CredentialUnavailableReason.OAuthNotConfigured, ex.Reason);
    }

    [Fact]
    public async Task SaveSignIn_StoresRefreshToken_AndServesAccessTokenWithoutRefresh()
    {
        var account = Account(AccountAuthMethod.MicrosoftOAuth2);
        var endpoint = RotatingEndpoint();
        var provider = Provider(endpoint);

        provider.SaveSignIn(account, new OAuthTokens("at-signin", "rt-signin", _time.Now.AddHours(1), account.Username));
        var credential = await provider.GetAsync(account);

        Assert.Equal("at-signin", credential.Secret);
        Assert.Equal("rt-signin", _store.TryGet(account.CredentialKey, account.Username));
        Assert.Empty(endpoint.Requests);
    }

    [Fact]
    public void SaveSignIn_WithoutRefreshToken_Throws()
    {
        var account = Account(AccountAuthMethod.MicrosoftOAuth2);

        Assert.Throws<OAuthException>(() =>
            Provider(RotatingEndpoint()).SaveSignIn(account, new OAuthTokens("at", null, _time.Now.AddHours(1), null)));
        Assert.Empty(_store.Entries);
    }

    [Fact]
    public async Task Forget_DropsCachedAccessToken()
    {
        var account = Account(AccountAuthMethod.MicrosoftOAuth2);
        var endpoint = RotatingEndpoint();
        var provider = Provider(endpoint);
        provider.SaveSignIn(account, new OAuthTokens("at-signin", "rt-signin", _time.Now.AddHours(1), null));

        provider.Forget(account);
        var credential = await provider.GetAsync(account);

        Assert.Equal("at-1", credential.Secret);
        Assert.Equal("rt-signin", Assert.Single(endpoint.Requests)["refresh_token"]);
    }
}
