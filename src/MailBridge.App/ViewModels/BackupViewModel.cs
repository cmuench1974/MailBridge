using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailBridge.Core.Models;
using MailKit;
using MailBridge.Core.Services;

namespace MailBridge.App.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly BackupService _backupService;
    private readonly CompressionService _compressionService;
    private readonly AccountCredentialProvider _credentials;
    private readonly ImapConnectionService _imapConnectionService;

    [ObservableProperty]
    private EmailAccount? selectedAccount;

    [ObservableProperty]
    private string destinationDirectory = string.Empty;

    [ObservableProperty]
    private bool compressToZip = true;

    /// <summary>Null until the first run; the page shows <see cref="StatusLine"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    private string? statusText;

    /// <summary>
    /// "Ready." is resolved on read: this view model is created before the UI language is applied
    /// at startup, and it outlives the window that is rebuilt when the language changes.
    /// </summary>
    public string StatusLine => StatusText ?? Localization.Strings.Get("backup.ready");

    /// <summary>0-100 for the progress bar.</summary>
    [ObservableProperty]
    private double progressValue;

    /// <summary>True while the amount of work is unknown (connecting, counting).</summary>
    [ObservableProperty]
    private bool isProgressIndeterminate = true;

    /// <summary>"x of y messages (z %)" below the progress bar; empty when there is nothing to count.</summary>
    [ObservableProperty]
    private string progressDetail = string.Empty;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private ObservableCollection<SelectableFolder> serverFolders = new();

    [ObservableProperty]
    private SelectableFolder? selectedServerFolder;

    [ObservableProperty]
    private bool isLoadingFolders;

    public string SelectionSummary
    {
        get
        {
            if (ServerFolders.Count == 0)
            {
                return Localization.Strings.Get("backup.selEverything");
            }

            var folders = ServerFolders.Count(f => f.IsChecked != false);
            var messages = ServerFolders
                .Where(f => f.IsChecked != false)
                .Sum(f => f.SelectedMessageCount);
            return Localization.Strings.Get("backup.selSummary", folders, ServerFolders.Count, messages);
        }
    }

    public BackupViewModel(BackupService backupService, CompressionService compressionService, AccountCredentialProvider credentials, SettingsStore settingsStore, ImapConnectionService imapConnectionService)
    {
        _backupService = backupService;
        _compressionService = compressionService;
        _credentials = credentials;
        _imapConnectionService = imapConnectionService;

        var settings = settingsStore.Load();
        if (!string.IsNullOrWhiteSpace(settings.DefaultBackupDirectory))
        {
            DestinationDirectory = settings.DefaultBackupDirectory;
        }
        CompressToZip = settings.DefaultCompressToZip;
    }

    /// <summary>
    /// A folder selection belongs to one account: kept across a switch, it would limit the new
    /// account's backup to the folders the old one happened to have.
    /// </summary>
    partial void OnSelectedAccountChanged(EmailAccount? value)
    {
        SelectedServerFolder = null;
        ServerFolders.Clear();
        OnPropertyChanged(nameof(SelectionSummary));
    }

    partial void OnSelectedServerFolderChanged(SelectableFolder? value)
    {
        if (value is not null && !value.MessagesLoaded)
        {
            _ = LoadFolderMessagesAsync(value);
        }
    }

    [RelayCommand]
    private async Task LoadFoldersAsync()
    {
        if (SelectedAccount is null)
        {
            StatusText = Localization.Strings.Get("backup.selNeedAccount");
            return;
        }

        IsLoadingFolders = true;
        StatusText = Localization.Strings.Get("backup.selLoading");

        try
        {
            var account = SelectedAccount;
            var credential = await _credentials.GetAsync(account);
            ServerFolders.Clear();
            SelectedServerFolder = null;
            var folders = new List<SelectableFolder>();

            using (var client = await _imapConnectionService.ConnectAsync(account, credential))
            {
                foreach (var folder in await _imapConnectionService.GetAllFoldersAsync(client))
                {
                    // STATUS counts without selecting the folder - cheaper with many (sub)folders.
                    await folder.StatusAsync(MailKit.StatusItems.Count);
                    folders.Add(new SelectableFolder { Name = folder.FullName, TotalMessages = folder.Count });
                }
            }

            if (!ReferenceEquals(account, SelectedAccount))
            {
                StatusText = string.Empty; // the account was switched while loading; this list would not fit
                return;
            }

            foreach (var f in folders)
            {
                f.SelectionChanged = () => { f.RefreshCheckedState(); OnPropertyChanged(nameof(SelectionSummary)); };
                ServerFolders.Add(f);
            }

            OnPropertyChanged(nameof(SelectionSummary));
            StatusText = Localization.Strings.Get("backup.selLoaded", folders.Count);
        }
        catch (CredentialUnavailableException ex)
        {
            StatusText = CredentialErrors.Describe(ex);
        }
        catch (Exception ex)
        {
            StatusText = Localization.Strings.Get("backup.failed", ex.Message);
        }
        finally
        {
            IsLoadingFolders = false;
        }
    }

    public async Task LoadFolderMessagesAsync(SelectableFolder folder)
    {
        if (SelectedAccount is null || folder.MessagesLoaded)
        {
            return;
        }

        try
        {
            var account = SelectedAccount;
            var credential = await _credentials.GetAsync(account);
            using var client = await _imapConnectionService.ConnectAsync(account, credential);
            var imapFolder = await client.GetFolderAsync(folder.Name);
            await imapFolder.OpenAsync(MailKit.FolderAccess.ReadOnly);
            var uids = await imapFolder.SearchAsync(MailKit.Search.SearchQuery.All);
            var summaries = await imapFolder.FetchAsync(
                uids,
                MailKit.MessageSummaryItems.Envelope | MailKit.MessageSummaryItems.UniqueId,
                CancellationToken.None);

            foreach (var summary in summaries)
            {
                var message = new SelectableMessage
                {
                    ItemKey = summary.UniqueId.Id.ToString(),
                    Subject = summary.Envelope?.Subject ?? "(no subject)",
                    From = summary.Envelope?.From?.ToString() ?? string.Empty,
                    DateText = summary.Envelope?.Date?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
                };
                message.SelectionChanged = folder.SelectionChanged;
                folder.Messages.Add(message);
            }

            folder.MessagesLoaded = true;
            OnPropertyChanged(nameof(SelectionSummary));
        }
        catch
        {
            // message list is a refinement, not a requirement; the folder
            // stays selected as a whole if loading fails
        }
    }

    private BackupSelection? BuildSelection()
    {
        if (ServerFolders.Count == 0)
        {
            return null;
        }

        var selection = new BackupSelection();
        foreach (var folder in ServerFolders)
        {
            var folderSelection = new FolderSelection { IncludeFolder = folder.IsChecked != false };

            if (folder.MessagesLoaded && folder.IsChecked != true)
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
    private async Task RunBackupAsync()
    {
        if (SelectedAccount is null || string.IsNullOrWhiteSpace(DestinationDirectory))
        {
            StatusText = Localization.Strings.Get("common.selectFirst");
            return;
        }

        // Snapshot: the form stays editable while the backup runs.
        var account = SelectedAccount;
        var destination = DestinationDirectory;
        var compress = CompressToZip;
        var stagingDir = compress
            ? Path.Combine(Path.GetTempPath(), "MailBridge", "staging_" + Guid.NewGuid().ToString("N"))
            : null;

        IsRunning = true;
        ShowIndeterminate(Localization.Strings.Get("backup.connecting"));

        try
        {
            var credential = await _credentials.GetAsync(account);
            var progress = new Progress<BackupProgress>(ShowBackupProgress);
            var manifest = await _backupService.RunBackupAsync(account, credential, stagingDir ?? destination, progress, selection: BuildSelection());

            if (stagingDir is not null)
            {
                var zipPath = Path.Combine(destination, $"{account.Username}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                ShowIndeterminate(Localization.Strings.Get("backup.compressing", Path.GetFileName(zipPath)));

                // Off the UI thread: packing a large backup takes minutes.
                var zipProgress = new Progress<CompressionProgress>(p =>
                {
                    if (IsRunning)
                    {
                        ShowCount(ProgressFormat.Percent(p.FilesDone, p.FilesTotal), ProgressFormat.Files(p.FilesDone, p.FilesTotal));
                    }
                });
                await Task.Run(() => _compressionService.CompressDirectory(stagingDir, zipPath, zipProgress));
                StatusText = Localization.Strings.Get("backup.completeZip", manifest.TotalMessages, zipPath);
            }
            else
            {
                StatusText = Localization.Strings.Get("backup.completeDir", manifest.TotalMessages, destination);
            }
        }
        catch (CredentialUnavailableException ex)
        {
            StatusText = CredentialErrors.Describe(ex);
        }
        catch (Exception ex)
        {
            StatusText = Localization.Strings.Get("backup.failed", ex.Message);
        }
        finally
        {
            IsRunning = false;
            ProgressDetail = string.Empty;

            if (stagingDir is not null)
            {
                // The downloaded copy is in the .zip now - or the backup failed; either way it goes.
                await Task.Run(() =>
                {
                    try
                    {
                        if (Directory.Exists(stagingDir))
                        {
                            Directory.Delete(stagingDir, recursive: true);
                        }
                    }
                    catch (IOException)
                    {
                        // left in %TEMP%; not worth failing the backup for
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                });
            }
        }
    }

    private void ShowBackupProgress(BackupProgress p)
    {
        if (!IsRunning)
        {
            return; // a late report after the run ended must not overwrite the result
        }

        switch (p.Phase)
        {
            case BackupPhase.Connecting:
                ShowIndeterminate(Localization.Strings.Get("backup.connecting"));
                break;
            case BackupPhase.Counting:
                ShowIndeterminate(Localization.Strings.Get("backup.counting"));
                break;
            default:
                StatusText = Localization.Strings.Get("progress.folderMessages", p.FolderNumber, p.FolderCount, p.Folder, p.MessagesDone, p.MessagesTotal);
                if (p.TotalMessages > 0)
                {
                    ShowCount(ProgressFormat.Percent(p.TotalMessagesDone, p.TotalMessages), ProgressFormat.Messages(p.TotalMessagesDone, p.TotalMessages));
                }
                else
                {
                    IsProgressIndeterminate = true;
                    ProgressDetail = string.Empty;
                }

                break;
        }
    }

    private void ShowIndeterminate(string status)
    {
        StatusText = status;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        ProgressDetail = string.Empty;
    }

    private void ShowCount(double percent, string detail)
    {
        IsProgressIndeterminate = false;
        ProgressValue = percent;
        ProgressDetail = detail;
    }
}
