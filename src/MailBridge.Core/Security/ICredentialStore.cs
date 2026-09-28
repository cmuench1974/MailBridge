namespace MailBridge.Core.Security;

/// <summary>
/// Stores account passwords/app-passwords outside of application data files.
/// The WinUI app implements this on top of the Windows Credential Locker
/// (PasswordVault); never persist secrets in JSON/XML config.
/// </summary>
public interface ICredentialStore
{
    void Save(string credentialKey, string username, string secret);

    string? TryGet(string credentialKey, string username);

    void Remove(string credentialKey, string username);
}
