namespace MailBridge.Core.Models;

/// <summary>
/// Connection details for an IMAP account. The password/app-password is never
/// stored on this object at rest - callers should resolve it from
/// <see cref="Security.ICredentialStore"/> just before connecting.
/// </summary>
public sealed class EmailAccount
{
    public string DisplayName { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 993;

    public bool UseSsl { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Key used to look up the password in the OS credential store.
    /// Never serialize the actual secret alongside account metadata.
    /// </summary>
    public string CredentialKey { get; set; } = string.Empty;
}
