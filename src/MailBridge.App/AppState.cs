using MailBridge.App.Scheduling;
using MailBridge.App.Security;
using MailBridge.App.ViewModels;
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

    private static readonly WindowsCredentialStore CredentialStore = new();
    private static readonly ImapConnectionService ImapConnectionService = new();
    private static readonly BackupService BackupServiceInstance = new(ImapConnectionService);
    private static readonly CompressionService CompressionServiceInstance = new();

    public static AccountStore AccountStore { get; } = new(Path.Combine(DataDirectory, "accounts.json"));

    public static ScheduleStore ScheduleStore { get; } = new(Path.Combine(DataDirectory, "schedules.json"));

    public static SettingsStore SettingsStoreInstance { get; } = new(Path.Combine(DataDirectory, "settings.json"));

    public static WindowsTaskSchedulerService TaskSchedulerService { get; } = new();

    public static ScheduledBackupRunner ScheduledBackupRunner { get; } =
        new(AccountStore, ScheduleStore, CredentialStore, BackupServiceInstance, CompressionServiceInstance);

    public static SettingsViewModel Settings { get; } = new(SettingsStoreInstance);

    public static AccountsViewModel Accounts { get; } = new(CredentialStore, AccountStore, ImapConnectionService);

    public static BackupViewModel Backup { get; } = new(BackupServiceInstance, CompressionServiceInstance, CredentialStore, SettingsStoreInstance);

    public static RestoreViewModel Restore { get; } = new(new RestoreService(ImapConnectionService), CompressionServiceInstance, CredentialStore);

    public static SchedulesViewModel Schedules { get; } = new(ScheduleStore, TaskSchedulerService, Accounts.Accounts, SettingsStoreInstance);
}
