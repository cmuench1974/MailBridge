using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailBridge.Core.Models;
using MailKit;
using MailBridge.Core.Security;
using MailBridge.Core.Services;

namespace MailBridge.App.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly BackupService _backupService;
    private readonly CompressionService _compressionService;
    private readonly ICredentialStore _credentialStore;
    private readonly ImapConnectionService _imapConnectionService;

    [ObservableProperty]
    private EmailAccount? selectedAccount;

    [ObservableProperty]
    private string destinationDirectory = string.Empty;

    [ObservableProperty]
    private bool compressToZip = true;

    [ObservableProperty]
    private string statusText = "Ready.";

    [ObservableProperty]
    private double progressValue;

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

    public BackupViewModel(BackupService backupService, CompressionService compressionService, ICredentialStore credentialStore, SettingsStore settingsStore, ImapConnectionService imapConnectionService)
    {
        _backupService = backupService;
        _compressionService = compressionService;
        _credentialStore = credentialStore;
        _imapConnectionService = imapConnectionService;

        var settings = settingsStore.Load();
        if (!string.IsNullOrWhiteSpace(settings.DefaultBackupDirectory))
        {
            DestinationDirectory = settings.DefaultBackupDirectory;
        }
        CompressToZip = settings.DefaultCompressToZip;
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

        var password = _credentialStore.TryGet(SelectedAccount.CredentialKey, SelectedAccount.Username);
        if (password is null)
        {
            StatusText = Localization.Strings.Get("common.noPassword");
            return;
        }

        IsLoadingFolders = true;
        StatusText = Localization.Strings.Get("backup.selLoading");

        try
        {
            var account = SelectedAccount;
            ServerFolders.Clear();
            SelectedServerFolder = null;
            var folders = new List<SelectableFolder>();

            using (var client = await _imapConnectionService.ConnectAsync(account, password))
            {
                foreach (var folder in await _imapConnectionService.GetAllFoldersAsync(client))
                {
                    var count = 0;
                    try
                    {
                        await folder.OpenAsync(MailKit.FolderAccess.ReadOnly);
                        count = folder.Count;
                    }
                    finally
                    {
                        await folder.CloseAsync(false);
                    }

                    folders.Add(new SelectableFolder { Name = folder.FullName, TotalMessages = count });
                }
            }

            foreach (var f in folders)
            {
                f.SelectionChanged = () => { f.RefreshCheckedState(); OnPropertyChanged(nameof(SelectionSummary)); };
                ServerFolders.Add(f);
            }

            OnPropertyChanged(nameof(SelectionSummary));
            StatusText = Localization.Strings.Get("backup.selLoaded", folders.Count);
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

    private async Task LoadFolderMessagesAsync(SelectableFolder folder)
    {
        if (SelectedAccount is null || folder.MessagesLoaded)
        {
            return;
        }

        var password = _credentialStore.TryGet(SelectedAccount.CredentialKey, SelectedAccount.Username);
        if (password is null)
        {
            return;
        }

        try
        {
            using var client = await _imapConnectionService.ConnectAsync(SelectedAccount, password);
            var folders = await _imapConnectionService.GetAllFoldersAsync(client);
            var imapFolder = folders.FirstOrDefault(f => f.FullName == folder.Name);
            if (imapFolder is null)
            {
                return;
            }

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

        var password = _credentialStore.TryGet(SelectedAccount.CredentialKey, SelectedAccount.Username);
        if (password is null)
        {
            StatusText = Localization.Strings.Get("common.noPassword");
            return;
        }

        IsRunning = true;
        StatusText = Localization.Strings.Get("backup.connecting");
        ProgressValue = 0;

        try
        {
            var workDir = CompressToZip
                ? Path.Combine(Path.GetTempPath(), "MailBridge", "staging_" + Guid.NewGuid().ToString("N"))
                : DestinationDirectory;

            var progress = new Progress<BackupProgress>(p =>
            {
                StatusText = $"{p.Folder}: {p.MessagesDone}/{p.MessagesTotal}";
            });

            var manifest = await _backupService.RunBackupAsync(SelectedAccount, password, workDir, progress, selection: BuildSelection());

            if (CompressToZip)
            {
                var zipPath = Path.Combine(DestinationDirectory, $"{SelectedAccount.Username}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                _compressionService.CompressDirectory(workDir, zipPath);
                Directory.Delete(workDir, recursive: true);
                StatusText = Localization.Strings.Get("backup.completeZip", manifest.TotalMessages, zipPath);
            }
            else
            {
                StatusText = Localization.Strings.Get("backup.completeDir", manifest.TotalMessages, DestinationDirectory);
            }
        }
        catch (Exception ex)
        {
            StatusText = Localization.Strings.Get("backup.failed", ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }
}
