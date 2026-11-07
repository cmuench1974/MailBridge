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

    /// <summary>Null until the first run; the page shows <see cref="StatusLine"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    private string? statusText;

    /// <summary>
    /// "Ready." is resolved on read: this view model is created before the UI language is applied
    /// at startup, and it outlives the window that is rebuilt when the language changes.
    /// </summary>
    public string StatusLine => StatusText ?? Localization.Strings.Get("restore.ready");

    [ObservableProperty]
    private bool isRunning;

    /// <summary>0-100 for the progress bar.</summary>
    [ObservableProperty]
    private double progressValue;

    /// <summary>True while the amount of work is unknown (extracting, connecting).</summary>
    [ObservableProperty]
    private bool isProgressIndeterminate = true;

    /// <summary>Counts below the progress bar; empty when there is nothing to count.</summary>
    [ObservableProperty]
    private string progressDetail = string.Empty;

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

        var account = TargetAccount;
        var backupPath = BackupPath;
        var uiContext = SynchronizationContext.Current;

        IsRunning = true;
        ShowIndeterminate(Localization.Strings.Get("restore.preparing"));

        string? extractedTempDir = null;

        try
        {
            var credential = await _credentials.GetAsync(account);

            var isZip = backupPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            if (isZip)
            {
                ShowIndeterminate(Localization.Strings.Get("restore.extracting"));
            }

            // Off the UI thread: extracting a large .zip takes a while.
            var workingDir = await Task.Run(EnsureWorkingDirectory);
            extractedTempDir = workingDir != backupPath ? workingDir : null;

            var filter = new RestoreFilter
            {
                FromContains = string.IsNullOrWhiteSpace(FromContains) ? null : FromContains,
                SubjectContains = string.IsNullOrWhiteSpace(SubjectContains) ? null : SubjectContains,
                DateFrom = DateFrom,
                DateTo = DateTo,
            };

            var outcome = await _restoreService.RunRestoreAsync(
                workingDir,
                account,
                credential,
                filter,
                conflict => AskOnUiThreadAsync(uiContext, conflict),
                selection: BuildSelection(),
                destination: BuildDestination(),
                progress: new Progress<RestoreProgress>(ShowRestoreProgress));

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
            IsRunning = false;
            ProgressDetail = string.Empty;

            if (extractedTempDir is not null)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        if (Directory.Exists(extractedTempDir))
                        {
                            Directory.Delete(extractedTempDir, recursive: true);
                        }
                    }
                    catch (IOException)
                    {
                        // left in %TEMP%; not worth failing the restore for
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                });
            }
        }
    }

    /// <summary>
    /// The restore runs on a pool thread after its first await, but the conflict dialog is a
    /// XAML element and must be created on the UI thread.
    /// </summary>
    private Task<ConflictResolution> AskOnUiThreadAsync(SynchronizationContext? uiContext, RestoreConflict conflict)
    {
        if (ConflictHandler is not { } handler)
        {
            return Task.FromResult(ConflictResolution.Skip);
        }

        if (uiContext is null || SynchronizationContext.Current == uiContext)
        {
            return handler(conflict);
        }

        var answer = new TaskCompletionSource<ConflictResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
        uiContext.Post(async _ =>
        {
            try
            {
                answer.SetResult(await handler(conflict));
            }
            catch (Exception ex)
            {
                answer.SetException(ex);
            }
        }, null);
        return answer.Task;
    }

    private void ShowRestoreProgress(RestoreProgress p)
    {
        if (!IsRunning)
        {
            return; // a late report after the run ended must not overwrite the result
        }

        var folderLabel = string.IsNullOrEmpty(p.TargetFolder) || p.TargetFolder == p.Folder
            ? p.Folder
            : $"{p.Folder} → {p.TargetFolder}";

        StatusText = p.Phase switch
        {
            RestorePhase.Connecting => Localization.Strings.Get("restore.connecting"),
            RestorePhase.CheckingFolder => Localization.Strings.Get("restore.checkingFolder", p.FolderNumber, p.FolderCount, p.TargetFolder),
            _ => Localization.Strings.Get("progress.folder", p.FolderNumber, p.FolderCount, folderLabel),
        };

        if (p.MessagesTotal == 0)
        {
            IsProgressIndeterminate = true;
            ProgressDetail = string.Empty;
            return;
        }

        // Checking a large target folder takes a while with nothing to count: keep the bar moving.
        IsProgressIndeterminate = p.Phase != RestorePhase.Restoring;
        ProgressValue = ProgressFormat.Percent(p.MessagesDone, p.MessagesTotal);
        ProgressDetail = ProgressFormat.Messages(p.MessagesDone, p.MessagesTotal)
                         + " · " + Localization.Strings.Get("restore.progressCounts", p.Restored, p.Replaced, p.Skipped);
    }

    private void ShowIndeterminate(string status)
    {
        StatusText = status;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        ProgressDetail = string.Empty;
    }
}
