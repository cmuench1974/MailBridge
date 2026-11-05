namespace MailBridge.Core.Models;

/// <summary>
/// How MailBridge signs in to an account's IMAP server. Persisted as a number in
/// accounts.json, so existing values must keep their numbers; accounts saved
/// before OAuth2 support have no value and load as <see cref="Password"/>.
/// </summary>
public enum AccountAuthMethod
{
    /// <summary>Username + password or app password, stored in the credential store.</summary>
    Password = 0,

    /// <summary>
    /// Microsoft account (Outlook.com, Microsoft 365) via OAuth2: the credential store
    /// holds the refresh token, IMAP authenticates with a short-lived access token (XOAUTH2).
    /// </summary>
    MicrosoftOAuth2 = 1,

    /// <summary>Google account (Gmail) via OAuth2, stored and used like <see cref="MicrosoftOAuth2"/>.</summary>
    GoogleOAuth2 = 2,
}
