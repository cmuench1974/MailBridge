namespace MailBridge.Core.Security.OAuth;

/// <summary>
/// Endpoints, scopes and app registration of one OAuth2 provider (authorization code flow
/// with PKCE for a desktop client).
/// </summary>
public sealed record OAuthProviderOptions(
    string DisplayName,
    string ClientId,
    Uri AuthorizationEndpoint,
    Uri TokenEndpoint,
    IReadOnlyList<string> Scopes)
{
    /// <summary>
    /// Client secret sent to the token endpoint, for providers that issue one to desktop apps
    /// (Google). It is embedded in the app and therefore not confidential (RFC 8252 8.5).
    /// </summary>
    public string? ClientSecret { get; init; }

    /// <summary>True when the provider rejects token requests without <see cref="ClientSecret"/>.</summary>
    public bool RequiresClientSecret { get; init; }

    public IReadOnlyDictionary<string, string>? ExtraAuthorizationParameters { get; init; }

    /// <summary>Whether code redemption and refresh repeat the scopes (Microsoft expects them, Google does not).</summary>
    public bool IncludeScopeInTokenRequests { get; init; } = true;

    /// <summary>False when the build lacks the client ID (or required secret); sign-in cannot work then.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && (!RequiresClientSecret || !string.IsNullOrWhiteSpace(ClientSecret));
}

/// <summary>Microsoft identity platform (v2.0) settings for IMAP access to Outlook.com and Microsoft 365.</summary>
public static class MicrosoftOAuth
{
    /// <summary>IMAP host for both Outlook.com and Microsoft 365 mailboxes.</summary>
    public const string ImapHost = "outlook.office365.com";

    /// <summary>
    /// "common" accepts personal Microsoft accounts and work/school accounts of any tenant,
    /// which requires the app registration to allow both account types.
    /// </summary>
    private const string Authority = "https://login.microsoftonline.com/common/oauth2/v2.0/";

    public static OAuthProviderOptions CreateOptions(string clientId) => new(
        "Microsoft",
        clientId.Trim(),
        new Uri(Authority + "authorize"),
        new Uri(Authority + "token"),
        new[]
        {
            "https://outlook.office.com/IMAP.AccessAsUser.All",
            "offline_access", // refresh token, so scheduled backups can sign in without the user
            "openid",         // id_token with the signed-in address, used as IMAP username
            "profile",
            "email",
        })
    {
        ExtraAuthorizationParameters = new Dictionary<string, string> { ["prompt"] = "select_account" },
    };
}

/// <summary>Google OAuth 2.0 settings for IMAP access to Gmail (and Google Workspace mail).</summary>
public static class GoogleOAuth
{
    public const string ImapHost = "imap.gmail.com";

    public static OAuthProviderOptions CreateOptions(string clientId, string clientSecret) => new(
        "Google",
        clientId.Trim(),
        new Uri("https://accounts.google.com/o/oauth2/v2/auth"),
        new Uri("https://oauth2.googleapis.com/token"),
        new[]
        {
            "https://mail.google.com/", // full IMAP access; the only scope Gmail accepts for IMAP
            "openid",                   // id_token with the signed-in address, used as IMAP username
            "email",
        })
    {
        ClientSecret = clientSecret.Trim(),
        RequiresClientSecret = true,
        IncludeScopeInTokenRequests = false,
        ExtraAuthorizationParameters = new Dictionary<string, string>
        {
            // Google issues a refresh token only for offline access, and only on a consent screen.
            ["access_type"] = "offline",
            ["prompt"] = "consent select_account",
        },
    };
}
