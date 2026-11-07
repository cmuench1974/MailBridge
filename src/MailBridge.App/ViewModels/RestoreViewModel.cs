using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailBridge.Core.Models;
using MailBridge.Core.Services;

namespace MailBridge.App.ViewModels;

public partial class RestoreViewModel : ObservableObject
{
    private readonly RestoreService _restoreService;
    private readonly CompressionService _compressionService;
    private readonly AccountCredentialProvider _credentials;
    private readonly ImapConnectionService _imapConnectionService;

    /// <summary>Bumped by every folder load and account switch, so only the latest load updates the list.</summary>
    private int _targetFolderLoad;

    [ObservableProperty]
    private string backupPath = string.Empty;

    [ObservableProperty]
    private EmailAccount? targetAccount;

    /// <summary>"Same folders as in the backup", followed by the target account's folders once loaded.</summary>
    public ObservableCollection<TargetFolderOption> TargetFolders { get; } = new() { TargetFolderOption.SameAsBackup };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargetFolder))]
    [NotifyPropertyChangedFor(nameof(TargetFolderHint))]
    private TargetFolderOption? selectedTargetFolder = TargetFolderOption.SameAsBackup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetFolderHint))]
    private bool keepFolderStructure;

    [ObservableProperty]
    private bool isLoadingTargetFolders;

    /// <summary>True when a folder of the target account is picked rather than "same as in the backup".</summary>
    public bool HasTargetFolder => SelectedTargetFolder?.FullName is not null;

    public string TargetFolderHint
    {
        get
        {
            if (SelectedTargetFolder is not { FullName: { } name } folder)
            {
                return Localization.Strings.Get("restore.targetFolderHintSameAsBackup");
            }

            if (!KeepFolderStructure)
            {
                return Localization.Strings.Get("restore.targetFolderHintInto", name);
            }

            var separator = folder.Separator == '\0' ? '/' : folder.Separator;
            return Localization.Strings.Get("restore.targetFolderHintBelow", name, $"{name}{separator}INBOX");
        }
    }

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

    public RestoreViewModel(RestoreService restoreService, CompressionService compressionService, AccountCredentialProvider credentials, ImapConnectionService imapConnectionService)
    {
        _restoreService = restoreService;
        _compressionService = compressionService;
        _credentials = credentials;
        _imapConnectionService = imapConnectionService;
    }

    /// <summary>
    /// Folders belong to one account: a folder picked for the previous account must not carry
    /// over, and the list is reloaded for the new one.
    /// </summary>
    partial void OnTargetAccountChanged(EmailAccount? value)
    {
        _targetFolderLoad++;
        IsLoadingTargetFolders = false;
        ReplaceTargetFolders(Array.Empty<TargetFolderOption>(), keepSelection: false);

        if (value is not null)
        {
            _ = LoadTargetFoldersAsync();
        }
    }

    [RelayCommand]
    private async Task LoadTargetFoldersAsync()
    {
        if (TargetAccount is not { } account)
        {
            StatusText = Localization.Strings.Get("restore.selectFirst");
            return;
        }

        var load = ++_targetFolderLoad;
        IsLoadingTargetFolders = true;
        try
        {
            var credential = await _credentials.GetAsync(account);
            List<TargetFolderOption> options;
            using (var client = await _imapConnectionService.ConnectAsync(account, credential))
            {
                options = (await _imapConnectionService.GetAllFoldersAsync(client))
                    .Select(f => new TargetFolderOption(f.FullName, f.DirectorySeparator))
                    .ToList();
            }

            if (load == _targetFolderLoad)
            {
                ReplaceTargetFolders(options, keepSelection: true);
            }
        }
        catch (CredentialUnavailableException ex)
        {
            if (load == _targetFolderLoad)
            {
                StatusText = CredentialErrors.Describe(ex);
            }
        }
        catch (Exception ex)
        {
            if (load == _targetFolderLoad)
            {
                StatusText = Localization.Strings.Get("restore.targetFoldersFailed", ex.Message);
            }
        }
        finally
        {
            if (load == _targetFolderLoad)
            {
                IsLoadingTargetFolders = false;
            }
        }
    }

    private void ReplaceTargetFolders(IReadOnlyList<TargetFolderOption> folders, bool keepSelection)
    {
        var previous = keepSelection ? SelectedTargetFolder?.FullName : null;

        // Select the fixed first entry before removing the rest, so the ComboBox never ends up
        // without a selection.
        SelectedTargetFolder = TargetFolderOption.SameAsBackup;
        while (TargetFolders.Count > 1)
        {
            TargetFolders.RemoveAt(TargetFolders.Count - 1);
        }

        foreach (var folder in folders)
        {
            TargetFolders.Add(folder);
        }

        SelectedTargetFolder = TargetFolders.FirstOrDefault(f => previous is not null && f.FullName == previous)
                               ?? TargetFolderOption.SameAsBackup;
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

    private RestoreDestination? BuildDestination() =>
        SelectedTargetFolder is { FullName: { } name } folder
            ? new RestoreDestination
            {
                FolderFullName = name,
                Separator = folder.Separator == '\0' ? null : folder.Separator,
                KeepFolderStructure = KeepFolderStructure,
            }
            : null;

    [RelayCommand]
    private async Task RunRestoreAsync()
    {
        if (TargetAccount is null || string.IsNullOrWhiteSpace(BackupPath))
        {
            StatusText = Localization.Strings.Get("restore.selectFirst");
            return;
        }

        IsRunning = true;
        StatusText = Localization.Strings.Get("restore.preparing");

        string? extractedTempDir = null;

        try
        {
            var credential = await _credentials.GetAsync(TargetAccount);
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
                credential,
                filter,
                conflict => ConflictHandler?.Invoke(conflict) ?? Task.FromResult(ConflictResolution.Skip),
                selection: BuildSelection(),
                destination: BuildDestination());

            StatusText = outcome.Aborted
                ? Localization.Strings.Get("restore.aborted", outcome.Restored, outcome.Replaced, outcome.Skipped)
                : Localization.Strings.Get("restore.done", outcome.Restored, outcome.Replaced, outcome.Skipped);
        }
        catch (CredentialUnavailableException ex)
        {
            StatusText = CredentialErrors.Describe(ex);
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
