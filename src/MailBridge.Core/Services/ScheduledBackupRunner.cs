using MailBridge.Core.Models;
using MailBridge.Core.Security;

namespace MailBridge.Core.Services;

/// <summary>
/// Executes a single <see cref="BackupSchedule"/> headlessly: resolves the
/// account and its stored password, runs <see cref="BackupService"/>,
/// optionally compresses the result, and records the outcome back into the
/// <see cref="ScheduleStore"/> so the UI can show when a schedule last ran
/// and whether it succeeded - this is what the Windows Task Scheduler
/// invokes via the app's --run-scheduled-backup command line flag.
/// </summary>
public sealed class ScheduledBackupRunner
{
    private readonly AccountStore _accountStore;
    private readonly ScheduleStore _scheduleStore;
    private readonly ICredentialStore _credentialStore;
    private readonly BackupService _backupService;
    private readonly CompressionService _compressionService;

    public ScheduledBackupRunner(
        AccountStore accountStore,
        ScheduleStore scheduleStore,
        ICredentialStore credentialStore,
        BackupService backupService,
        CompressionService compressionService)
    {
        _accountStore = accountStore;
        _scheduleStore = scheduleStore;
        _credentialStore = credentialStore;
        _backupService = backupService;
        _compressionService = compressionService;
    }

    public async Task RunAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var schedules = _scheduleStore.Load();
        var schedule = schedules.FirstOrDefault(s => s.Id == scheduleId);
        if (schedule is null)
        {
            return;
        }

        try
        {
            var account = _accountStore.Load().FirstOrDefault(a => a.Id == schedule.AccountId)
                          ?? throw new InvalidOperationException("The account for this schedule no longer exists.");

            var password = _credentialStore.TryGet(account.CredentialKey, account.Username)
                           ?? throw new InvalidOperationException("No stored password found for this account.");

            var workDir = schedule.CompressToZip
                ? Path.Combine(Path.GetTempPath(), "MailBridge", "scheduled_" + Guid.NewGuid().ToString("N"))
                : schedule.DestinationDirectory;

            var manifest = await _backupService.RunBackupAsync(account, password, workDir, progress: null, cancellationToken).ConfigureAwait(false);

            if (schedule.CompressToZip)
            {
                var zipPath = Path.Combine(schedule.DestinationDirectory, $"{account.Username}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                _compressionService.CompressDirectory(workDir, zipPath);
                Directory.Delete(workDir, recursive: true);
            }

            schedule.LastRunUtc = DateTimeOffset.UtcNow;
            schedule.LastRunSucceeded = true;
            schedule.LastRunMessage = $"Backed up {manifest.TotalMessages} messages.";
        }
        catch (Exception ex)
        {
            schedule.LastRunUtc = DateTimeOffset.UtcNow;
            schedule.LastRunSucceeded = false;
            schedule.LastRunMessage = ex.Message;
        }

        _scheduleStore.Upsert(schedule);
    }
}
