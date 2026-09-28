using MailBridge.Core.Security;
using Windows.Security.Credentials;

namespace MailBridge.App.Security;

/// <summary>
/// Stores account secrets in the Windows Credential Locker (PasswordVault)
/// instead of any application file, so backups/config never contain
/// plaintext passwords.
/// </summary>
public sealed class WindowsCredentialStore : ICredentialStore
{
    private readonly PasswordVault _vault = new();

    public void Save(string credentialKey, string username, string secret)
    {
        Remove(credentialKey, username);
        _vault.Add(new PasswordCredential(credentialKey, username, secret));
    }

    public string? TryGet(string credentialKey, string username)
    {
        try
        {
            var credential = _vault.Retrieve(credentialKey, username);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            return null;
        }
    }

    public void Remove(string credentialKey, string username)
    {
        try
        {
            var credential = _vault.Retrieve(credentialKey, username);
            _vault.Remove(credential);
        }
        catch
        {
            // Nothing stored for this key - nothing to remove.
        }
    }
}
