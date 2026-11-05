namespace MailBridge.Core.Security.OAuth;

/// <summary>Result of a token request. <see cref="ToString"/> never shows the tokens.</summary>
public sealed class OAuthTokens
{
    public OAuthTokens(string accessToken, string? refreshToken, DateTimeOffset expiresAtUtc, string? username)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresAtUtc = expiresAtUtc;
        Username = username;
    }

    public string AccessToken { get; }

    /// <summary>Null when a refresh response did not rotate the refresh token (keep the old one then).</summary>
    public string? RefreshToken { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    /// <summary>Email address of the signed-in account from the id_token, if one was returned.</summary>
    public string? Username { get; }

    public override string ToString() => $"OAuth tokens for {Username ?? "(unknown)"}, expires {ExpiresAtUtc:u}";
}

/// <summary>An error returned by the authorization server or the browser redirect.</summary>
public sealed class OAuthException : Exception
{
    // Errors after which only an interactive sign-in helps (RFC 6749 5.2, OpenID Connect 3.1.2.6).
    private static readonly HashSet<string> SignInErrors = new(StringComparer.Ordinal)
    {
        "invalid_grant", "interaction_required", "login_required", "consent_required",
    };

    public OAuthException(string error, string? description = null, Exception? innerException = null)
        : base(string.IsNullOrWhiteSpace(description) ? error : $"{description} ({error})", innerException)
    {
        Error = error;
    }

    /// <summary>OAuth2 error code, e.g. "invalid_grant" or "access_denied".</summary>
    public string Error { get; }

    /// <summary>True when the stored sign-in is no longer valid and the user has to sign in again.</summary>
    public bool RequiresSignIn => SignInErrors.Contains(Error);
}
