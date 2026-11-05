namespace MailBridge.Core.Models;

/// <summary>What a user has to do at the provider before a password login works.</summary>
public enum MailProviderSetup
{
    /// <summary>Works with the normal account password.</summary>
    None,

    /// <summary>IMAP access has to be switched on in the provider's webmail settings first.</summary>
    EnableImap,

    /// <summary>The normal password is rejected; a separate app password is required.</summary>
    AppPassword,
}

/// <summary>Preset IMAP connection details for a mail provider.</summary>
public sealed record MailProviderTemplate(string Name, string Host, MailProviderSetup Setup, int Port = 993)
{
    private static readonly AccountAuthMethod[] PasswordOnly = { AccountAuthMethod.Password };

    /// <summary>
    /// Sign-in methods the provider accepts, preferred first. <see cref="Setup"/> only applies to
    /// <see cref="AccountAuthMethod.Password"/>.
    /// </summary>
    public IReadOnlyList<AccountAuthMethod> AuthMethods { get; init; } = PasswordOnly;
}
