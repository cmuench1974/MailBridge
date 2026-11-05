using MailBridge.Core.Models;
using MailBridge.Core.Security;
using MailBridge.Core.Security.OAuth;

namespace MailBridge.Core.Services;

/// <summary>Why <see cref="AccountCredentialProvider"/> could not produce a credential.</summary>
public enum CredentialUnavailableReason
{
    /// <summary>A password account has no stored password.</summary>
    MissingPassword,

    /// <summary>An OAuth2 account has no stored sign-in, or the provider rejected it (expired/revoked).</summary>
    SignInRequired,

    /// <summary>This build has no OAuth2 client ID for the account's provider.</summary>
    OAuthNotConfigured,
}

/// <summary>Thrown when an account cannot be signed in without the user doing something first.</summary>
public sealed class CredentialUnavailableException : Exception
{
    public CredentialUnavailableException(CredentialUnavailableReason reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public CredentialUnavailableReason Reason { get; }
}

/// <summary>
/// Resolves the <see cref="ImapCredential"/> for an account just before connecting - from the
/// UI as well as from headless scheduled backups. Password accounts read the stored password.
/// OAuth2 accounts keep their refresh token in the credential store and get access tokens from
/// it, cached in memory until shortly before they expire; rotated refresh tokens are saved.
/// </summary>
public sealed class AccountCredentialProvider
{
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);

    private readonly ICredentialStore _store;
    private readonly OAuthClient _oauthClient;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, CachedAccessToken> _cache = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AccountCredentialProvider(ICredentialStore store, OAuthClient oauthClient, OAuthProviderOptions microsoftOptions, TimeProvider? timeProvider = null)
    {
        _store = store;
        _oauthClient = oauthClient;
        MicrosoftOptions = microsoftOptions;
        _time = timeProvider ?? TimeProvider.System;
    }

    public OAuthProviderOptions MicrosoftOptions { get; }

    public async Task<ImapCredential> GetAsync(EmailAccount account, CancellationToken cancellationToken = default)
    {
        if (account.AuthMethod == AccountAuthMethod.Password)
        {
            var password = _store.TryGet(account.CredentialKey, account.Username)
                           ?? throw new CredentialUnavailableException(CredentialUnavailableReason.MissingPassword, "No stored password found for this account.");
            return ImapCredential.FromPassword(password);
        }

        if (!MicrosoftOptions.IsConfigured)
        {
            throw new CredentialUnavailableException(CredentialUnavailableReason.OAuthNotConfigured,
                "Microsoft sign-in is not configured in this build (no OAuth2 client ID).");
        }

        var key = CacheKey(account);
        if (TryGetCached(key) is { } cached)
        {
            return ImapCredential.FromAccessToken(cached);
        }

        // One refresh at a time: parallel connects (e.g. loading several folders) share the result.
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetCached(key) is { } refreshedMeanwhile)
            {
                return ImapCredential.FromAccessToken(refreshedMeanwhile);
            }

            var refreshToken = _store.TryGet(account.CredentialKey, account.Username)
                               ?? throw SignInRequired("No stored Microsoft sign-in found for this account.");

            OAuthTokens tokens;
            try
            {
                tokens = await _oauthClient.RefreshAsync(MicrosoftOptions, refreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch (OAuthException ex) when (ex.RequiresSignIn)
            {
                throw SignInRequired("The Microsoft sign-in for this account has expired or was revoked. Sign in again on the Accounts page.", ex);
            }

            if (tokens.RefreshToken is { } rotated && rotated != refreshToken)
            {
                _store.Save(account.CredentialKey, account.Username, rotated);
            }

            Cache(key, tokens);
            return ImapCredential.FromAccessToken(tokens.AccessToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Keeps the result of an interactive sign-in for the account: the refresh token goes to the
    /// credential store under the account's key, the access token into the in-memory cache.
    /// </summary>
    public void SaveSignIn(EmailAccount account, OAuthTokens tokens)
    {
        if (string.IsNullOrEmpty(tokens.RefreshToken))
        {
            throw new OAuthException("no_refresh_token", "The sign-in returned no refresh token, so MailBridge could not stay signed in.");
        }

        _store.Save(account.CredentialKey, account.Username, tokens.RefreshToken);
        Cache(CacheKey(account), tokens);
    }

    /// <summary>Drops the cached access token, e.g. after the account was edited or removed.</summary>
    public void Forget(EmailAccount account)
    {
        lock (_cache)
        {
            _cache.Remove(CacheKey(account));
        }
    }

    private string? TryGetCached(string key)
    {
        lock (_cache)
        {
            return _cache.TryGetValue(key, out var cached) && cached.ExpiresAtUtc - ExpiryMargin > _time.GetUtcNow()
                ? cached.AccessToken
                : null;
        }
    }

    private void Cache(string key, OAuthTokens tokens)
    {
        lock (_cache)
        {
            _cache[key] = new CachedAccessToken(tokens.AccessToken, tokens.ExpiresAtUtc);
        }
    }

    private static string CacheKey(EmailAccount account) => account.CredentialKey + "\n" + account.Username;

    private static CredentialUnavailableException SignInRequired(string message, Exception? inner = null) =>
        new(CredentialUnavailableReason.SignInRequired, message, inner);

    private sealed record CachedAccessToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
}
