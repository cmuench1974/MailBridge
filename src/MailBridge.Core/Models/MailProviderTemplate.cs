namespace MailBridge.Core.Models;

/// <summary>What a user has to do at the provider before MailBridge can log in.</summary>
public enum MailProviderSetup
{
    /// <summary>Works with the normal account password.</summary>
    None,

    /// <summary>IMAP access has to be switched on in the provider's webmail settings first.</summary>
    EnableImap,

    /// <summary>The normal password is rejected; a separate app password is required.</summary>
    AppPassword,

    /// <summary>Password login is disabled; the user signs in with Microsoft in the browser (OAuth2).</summary>
    MicrosoftOAuth2,
}

/// <summary>Preset IMAP connection details for a mail provider.</summary>
public sealed record MailProviderTemplate(string Name, string Host, MailProviderSetup Setup, int Port = 993)
{
    /// <summary>The sign-in method accounts created from this template use.</summary>
    public AccountAuthMethod AuthMethod => Setup == MailProviderSetup.MicrosoftOAuth2
        ? AccountAuthMethod.MicrosoftOAuth2
        : AccountAuthMethod.Password;
}
