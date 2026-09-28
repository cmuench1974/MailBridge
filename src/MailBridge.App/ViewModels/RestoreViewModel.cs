using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailBridge.Core.Models;
using MailBridge.Core.Security;
using MailBridge.Core.Services;

namespace MailBridge.App.ViewModels;

public partial class RestoreViewModel : ObservableObject
{
    private readonly RestoreService _restoreService;
    private readonly CompressionService _compressionService;
    private readonly ICredentialStore _credentialStore;

    [ObservableProperty]
    private string backupPath = string.Empty;

    [ObservableProperty]
    private EmailAccount? targetAccount;

    [ObservableProperty]
    private string fromContains = string.Empty;

    [ObservableProperty]
    private string subjectContains = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? dateFrom;

    [ObservableProperty]
    private DateTimeOffset? dateTo;

    [ObservableProperty]
    private string statusText = "Ready.";

    [ObservableProperty]
    private bool isRunning;

    /// <summary>
    /// Wired up by the view to show <see cref="Views.ConflictDialog"/> and
    /// return the user's decision. Kept as a delegate so the view model has
    /// no direct dependency on XAML/UI types.
    /// </summary>
    public Func<RestoreConflict, Task<ConflictResolution>>? ConflictHandler { get; set; }

    public RestoreViewModel(RestoreService restoreService, CompressionService compressionService, ICredentialStore credentialStore)
    {
        _restoreService = restoreService;
        _compressionService = compressionService;
        _credentialStore = credentialStore;
    }

    [RelayCommand]
    private async Task RunRestoreAsync()
    {
        if (TargetAccount is null || string.IsNullOrWhiteSpace(BackupPath))
        {
            StatusText = "Select a backup and a target account first.";
            return;
        }

        var password = _credentialStore.TryGet(TargetAccount.CredentialKey, TargetAccount.Username);
        if (password is null)
        {
            StatusText = "No stored password for this account - re-add it with a password.";
            return;
        }

        IsRunning = true;
        StatusText = "Preparing backup...";

        string? extractedTempDir = null;

        try
        {
            var workingDir = BackupPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? extractedTempDir = _compressionService.ExtractToTempDirectory(BackupPath)
                : BackupPath;

            var filter = new RestoreFilter
            {
                FromContains = string.IsNullOrWhiteSpace(FromContains) ? null : FromContains,
                SubjectContains = string.IsNullOrWhiteSpace(SubjectContains) ? null : SubjectContains,
                DateFrom = DateFrom,
                DateTo = DateTo,
            };

            StatusText = "Restoring...";

            var outcome = await _restoreService.RunRestoreAsync(
                workingDir,
                TargetAccount,
                password,
                filter,
                conflict => ConflictHandler?.Invoke(conflict) ?? Task.FromResult(ConflictResolution.Skip));

            StatusText = outcome.Aborted
                ? $"Aborted. Restored: {outcome.Restored}, Replaced: {outcome.Replaced}, Skipped: {outcome.Skipped}"
                : $"Done. Restored: {outcome.Restored}, Replaced: {outcome.Replaced}, Skipped: {outcome.Skipped}";
        }
        catch (Exception ex)
        {
            StatusText = $"Restore failed: {ex.Message}";
        }
        finally
        {
            if (extractedTempDir is not null && Directory.Exists(extractedTempDir))
            {
                Directory.Delete(extractedTempDir, recursive: true);
            }

            IsRunning = false;
        }
    }
}
