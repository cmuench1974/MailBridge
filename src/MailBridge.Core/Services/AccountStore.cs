using MailBridge.Core.Models;

namespace MailBridge.Core.Services;

/// <summary>
/// Persists account metadata (host, username, display name, credential key)
/// so accounts survive app restarts and scheduled backups can run without
/// the UI being open. Passwords are never stored here - only the
/// <see cref="EmailAccount.CredentialKey"/> used to look them up in
/// <see cref="Security.ICredentialStore"/>.
/// </summary>
public sealed class AccountStore
{
    private readonly string _path;

    public AccountStore(string path)
    {
        _path = path;
    }

    public List<EmailAccount> Load() => JsonFileStore.Load<EmailAccount>(_path);

    public void Save(IEnumerable<EmailAccount> accounts) => JsonFileStore.Save(_path, accounts.ToList());
}
