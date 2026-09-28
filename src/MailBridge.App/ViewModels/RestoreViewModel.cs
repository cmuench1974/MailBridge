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
    private string statusText = Localization.Strings.Get("restore.ready");

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
            StatusText = Localization.Strings.Get("restore.selectFirst");
            return;
        }

        var password = _credentialStore.TryGet(TargetAccount.CredentialKey, TargetAccount.Username);
        if (password is null)
        {
            StatusText = Localization.Strings.Get("common.noPassword");
            return;
        }

        IsRunning = true;
        StatusText = Localization.Strings.Get("restore.preparing");

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

            StatusText = Localization.Strings.Get("restore.restoring");

            var outcome = await _restoreService.RunRestoreAsync(
                workingDir,
                TargetAccount,
                password,
                filter,
                conflict => ConflictHandler?.Invoke(conflict) ?? Task.FromResult(ConflictResolution.Skip));

            StatusText = outcome.Aborted
                ? Localization.Strings.Get("restore.aborted", outcome.Restored, outcome.Replaced, outcome.Skipped)
                : Localization.Strings.Get("restore.done", outcome.Restored, outcome.Replaced, outcome.Skipped);
        }
        catch (Exception ex)
        {
            StatusText = Localization.Strings.Get("restore.failed", ex.Message);
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
