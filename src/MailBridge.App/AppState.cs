using MailBridge.App.Security;
using MailBridge.App.ViewModels;
using MailBridge.Core.Services;

namespace MailBridge.App;

/// <summary>
/// Minimal shared-instance holder so the Accounts list created on one page
/// is visible from Backup/Restore. A proper DI container can replace this
/// once the app grows beyond three pages.
/// </summary>
public static class AppState
{
    private static readonly WindowsCredentialStore CredentialStore = new();
    private static readonly ImapConnectionService ImapConnectionService = new();

    public static AccountsViewModel Accounts { get; } = new(CredentialStore);

    public static BackupViewModel Backup { get; } = new(new BackupService(ImapConnectionService), new CompressionService(), CredentialStore);

    public static RestoreViewModel Restore { get; } = new(new RestoreService(ImapConnectionService), new CompressionService(), CredentialStore);
}
