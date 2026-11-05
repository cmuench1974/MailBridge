namespace MailBridge.Core.Models;

/// <summary>
/// The secret <see cref="Services.ImapConnectionService"/> authenticates with: either a
/// password or an OAuth2 access token. Resolved just before connecting by
/// <see cref="Services.AccountCredentialProvider"/>; never persisted.
/// </summary>
public sealed class ImapCredential
{
    private ImapCredential(string secret, bool isOAuth2)
    {
        Secret = secret;
        IsOAuth2 = isOAuth2;
    }

    public string Secret { get; }

    /// <summary>True when <see cref="Secret"/> is an OAuth2 access token (SASL XOAUTH2).</summary>
    public bool IsOAuth2 { get; }

    public static ImapCredential FromPassword(string password) => new(password, isOAuth2: false);

    public static ImapCredential FromAccessToken(string accessToken) => new(accessToken, isOAuth2: true);

    // Keep the secret out of logs, debugger displays and exception messages.
    public override string ToString() => IsOAuth2 ? "OAuth2 access token" : "Password";
}
