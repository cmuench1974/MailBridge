namespace MailBridge.Core.Models;

/// <summary>
/// Connection details for an IMAP account. The secret (password/app-password, or the
/// OAuth2 refresh token for <see cref="AccountAuthMethod.MicrosoftOAuth2"/>) is never
/// stored on this object at rest - callers resolve an <see cref="ImapCredential"/> via
/// <see cref="Services.AccountCredentialProvider"/> just before connecting.
/// </summary>
public sealed class EmailAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string DisplayName { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 993;

    public bool UseSsl { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public AccountAuthMethod AuthMethod { get; set; } = AccountAuthMethod.Password;

    /// <summary>
    /// Key used to look up the secret in the OS credential store.
    /// Never serialize the actual secret alongside account metadata.
    /// </summary>
    public string CredentialKey { get; set; } = string.Empty;
}
