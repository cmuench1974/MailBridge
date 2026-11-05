using MailBridge.App.Scheduling;
using MailBridge.App.Security;
using MailBridge.App.Services;
using MailBridge.App.ViewModels;
using MailBridge.Core.Security.OAuth;
using MailBridge.Core.Services;

namespace MailBridge.App;

/// <summary>
/// Minimal shared-instance holder so state created on one page (accounts,
/// schedules, settings) is visible from every other page. A proper DI
/// container can replace this once the app grows further.
/// </summary>
public static class AppState
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MailBridge");

    // Static initializers run in textual order: keep dependencies above their users.
    private static readonly WindowsCredentialStore CredentialStore = new();
    private static readonly ImapConnectionService ImapConnectionService = new();
    private static readonly BackupService BackupServiceInstance = new(ImapConnectionService);
    private static readonly CompressionService CompressionServiceInstance = new();
    private static readonly OAuthClient OAuthClientInstance = new(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
    private static readonly OAuthSignInFlow SignInFlow = new(OAuthClientInstance);

    /// <summary>Passwords and OAuth2 access tokens for every connection, UI and scheduled backups alike.</summary>
    private static readonly AccountCredentialProvider Credentials =
        new(CredentialStore, OAuthClientInstance, OAuthConfiguration.Providers);

    public static AccountStore AccountStore { get; } = new(Path.Combine(DataDirectory, "accounts.json"));

    public static ScheduleStore ScheduleStore { get; } = new(Path.Combine(DataDirectory, "schedules.json"));

    public static SettingsStore SettingsStoreInstance { get; } = new(Path.Combine(DataDirectory, "settings.json"));

    public static WindowsTaskSchedulerService TaskSchedulerService { get; } = new();

    public static ScheduledBackupRunner ScheduledBackupRunner { get; } =
        new(AccountStore, ScheduleStore, Credentials, BackupServiceInstance, CompressionServiceInstance);

    public static ThemeService Theme { get; } = new();

    public static SettingsViewModel Settings { get; } = new(SettingsStoreInstance);

    public static AccountsViewModel Accounts { get; } = new(CredentialStore, Credentials, SignInFlow, AccountStore, ImapConnectionService);

    public static BackupViewModel Backup { get; } = new(BackupServiceInstance, CompressionServiceInstance, Credentials, SettingsStoreInstance, ImapConnectionService);

    public static RestoreViewModel Restore { get; } = new(new RestoreService(ImapConnectionService), CompressionServiceInstance, Credentials);

    public static SchedulesViewModel Schedules { get; } = new(ScheduleStore, TaskSchedulerService, Accounts.Accounts, SettingsStoreInstance);
}
