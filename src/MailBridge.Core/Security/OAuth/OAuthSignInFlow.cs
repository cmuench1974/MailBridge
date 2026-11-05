namespace MailBridge.Core.Security.OAuth;

/// <summary>
/// Interactive sign-in: opens the provider's login page in the user's browser, receives the
/// redirect on a loopback listener and redeems the code (authorization code flow + PKCE).
/// </summary>
public sealed class OAuthSignInFlow
{
    private readonly OAuthClient _client;

    public OAuthSignInFlow(OAuthClient client)
    {
        _client = client;
    }

    /// <param name="openBrowser">Opens the given URL in the user's default browser.</param>
    /// <param name="loginHint">Pre-fills the account name on the login page, if known.</param>
    /// <exception cref="OAuthException">The user declined, or the provider returned an error.</exception>
    /// <exception cref="OperationCanceledException">Cancelled, e.g. by a timeout, before the redirect arrived.</exception>
    public async Task<OAuthTokens> SignInAsync(
        OAuthProviderOptions options,
        Func<Uri, Task> openBrowser,
        BrowserPageText page,
        string? loginHint,
        CancellationToken cancellationToken)
    {
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException($"{options.DisplayName} sign-in is not configured in this build (no OAuth2 client ID).");
        }

        var codeVerifier = Pkce.CreateCodeVerifier();
        var state = Pkce.CreateState();

        using var listener = LoopbackRedirectListener.Start();
        var authorizationUri = _client.BuildAuthorizationUri(options, listener.RedirectUri, state, Pkce.CreateCodeChallenge(codeVerifier), loginHint);

        // Wait first, then open the browser: openBrowser may itself await the redirect's answer.
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var responseTask = listener.WaitForResponseAsync(page, waitCancellation.Token);
        try
        {
            await openBrowser(authorizationUri).ConfigureAwait(false);
        }
        catch
        {
            waitCancellation.Cancel();
            try
            {
                await responseTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: nothing to wait for once the browser could not be opened.
            }

            throw;
        }

        var response = await responseTask.ConfigureAwait(false);

        if (response.Error is not null)
        {
            throw new OAuthException(response.Error, response.ErrorDescription);
        }

        if (!string.Equals(response.State, state, StringComparison.Ordinal))
        {
            throw new OAuthException("invalid_state", "The sign-in response does not belong to this sign-in attempt.");
        }

        return await _client.RedeemCodeAsync(options, response.Code!, listener.RedirectUri, codeVerifier, cancellationToken).ConfigureAwait(false);
    }
}
