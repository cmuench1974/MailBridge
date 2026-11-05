namespace MailBridge.Core.Security.OAuth;

/// <summary>
/// Endpoints, scopes and app registration of one OAuth2 provider (authorization code flow
/// with PKCE for a public desktop client - there is no client secret).
/// </summary>
public sealed record OAuthProviderOptions(
    string ClientId,
    Uri AuthorizationEndpoint,
    Uri TokenEndpoint,
    IReadOnlyList<string> Scopes,
    IReadOnlyDictionary<string, string>? ExtraAuthorizationParameters = null)
{
    /// <summary>False when the build has no client ID; sign-in cannot work then.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
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
        },
        new Dictionary<string, string> { ["prompt"] = "select_account" });
}
