using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private ObservableCollection<SelectableFolder> backupFolders = new();

    [ObservableProperty]
    private SelectableFolder? selectedBackupFolder;

    [ObservableProperty]
    private bool isLoadingContents;

    public string SelectionSummary
    {
        get
        {
            if (BackupFolders.Count == 0)
            {
                return Localization.Strings.Get("restore.selEverything");
            }

            var folders = BackupFolders.Count(f => f.IsChecked != false);
            var messages = BackupFolders
                .Where(f => f.IsChecked != false)
                .Sum(f => f.SelectedMessageCount);
            return Localization.Strings.Get("restore.selSummary", folders, BackupFolders.Count, messages);
        }
    }

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

    partial void OnSelectedBackupFolderChanged(SelectableFolder? value)
    {
        // Backup contents are fully known from the index files - no network
        // needed, so "loading" is just showing them.
        if (value is not null && !value.MessagesLoaded)
        {
            value.MessagesLoaded = true;
            OnPropertyChanged(nameof(SelectionSummary));
        }
    }

    /// <summary>
    /// Resolves the backup path to a plain directory (extracting a .zip to a
    /// cached temp dir once) and reads manifest + indexes for the picker.
    /// </summary>
    [RelayCommand]
    private async Task LoadContentsAsync()
    {
        if (string.IsNullOrWhiteSpace(BackupPath))
        {
            StatusText = Localization.Strings.Get("restore.selectFirst");
            return;
        }

        IsLoadingContents = true;
        try
        {
            var workingDir = await Task.Run(() => EnsureWorkingDirectory());
            var contents = await _restoreService.GetBackupContentsAsync(workingDir);

            BackupFolders.Clear();
            SelectedBackupFolder = null;
            foreach (var (folderName, records) in contents.Folders)
            {
                var folder = new SelectableFolder { Name = folderName, TotalMessages = records.Count };
                folder.SelectionChanged = () => { folder.RefreshCheckedState(); OnPropertyChanged(nameof(SelectionSummary)); };
                foreach (var record in records)
                {
                    var message = new SelectableMessage
                    {
                        ItemKey = record.RelativeEmlPath,
                        Subject = record.Subject ?? "(no subject)",
                        From = record.From ?? string.Empty,
                        DateText = record.Date?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
                    };
                    message.SelectionChanged = folder.SelectionChanged;
                    folder.Messages.Add(message);
                }

                folder.MessagesLoaded = true;
                BackupFolders.Add(folder);
            }

            OnPropertyChanged(nameof(SelectionSummary));
            StatusText = Localization.Strings.Get("restore.selLoaded", contents.Folders.Count, contents.TotalMessages);
        }
        catch (Exception ex)
        {
            StatusText = Localization.Strings.Get("restore.failed", ex.Message);
        }
        finally
        {
            IsLoadingContents = false;
        }
    }

    private string? _extractedDir;
    private string? _extractedFrom;

    private string EnsureWorkingDirectory()
    {
        if (!BackupPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return BackupPath;
        }

        if (_extractedDir is not null && _extractedFrom == BackupPath && Directory.Exists(_extractedDir))
        {
            return _extractedDir;
        }

        _extractedDir = _compressionService.ExtractToTempDirectory(BackupPath);
        _extractedFrom = BackupPath;
        return _extractedDir;
    }

    private BackupSelection? BuildSelection()
    {
        if (BackupFolders.Count == 0)
        {
            return null;
        }

        var selection = new BackupSelection();
        foreach (var folder in BackupFolders)
        {
            var folderSelection = new FolderSelection { IncludeFolder = folder.IsChecked != false };

            if (folder.IsChecked != true)
            {
                folderSelection.IncludedItems = folder.Messages
                    .Where(m => m.IsChecked)
                    .Select(m => m.ItemKey)
                    .ToHashSet();
            }

            selection.Folders[folder.Name] = folderSelection;
        }

        return selection;
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
            var workingDir = EnsureWorkingDirectory();
            extractedTempDir = workingDir != BackupPath ? workingDir : null;

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
                conflict => ConflictHandler?.Invoke(conflict) ?? Task.FromResult(ConflictResolution.Skip),
                selection: BuildSelection());

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
