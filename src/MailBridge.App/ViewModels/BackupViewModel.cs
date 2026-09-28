using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailBridge.Core.Models;
using MailBridge.Core.Security;
using MailBridge.Core.Services;

namespace MailBridge.App.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly BackupService _backupService;
    private readonly CompressionService _compressionService;
    private readonly ICredentialStore _credentialStore;

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

    public BackupViewModel(BackupService backupService, CompressionService compressionService, ICredentialStore credentialStore, SettingsStore settingsStore)
    {
        _backupService = backupService;
        _compressionService = compressionService;
        _credentialStore = credentialStore;

        var settings = settingsStore.Load();
        if (!string.IsNullOrWhiteSpace(settings.DefaultBackupDirectory))
        {
            DestinationDirectory = settings.DefaultBackupDirectory;
        }
        CompressToZip = settings.DefaultCompressToZip;
    }

    [RelayCommand]
    private async Task RunBackupAsync()
    {
        if (SelectedAccount is null || string.IsNullOrWhiteSpace(DestinationDirectory))
        {
            StatusText = "Select an account and destination folder first.";
            return;
        }

        var password = _credentialStore.TryGet(SelectedAccount.CredentialKey, SelectedAccount.Username);
        if (password is null)
        {
            StatusText = "No stored password for this account - re-add it with a password.";
            return;
        }

        IsRunning = true;
        StatusText = "Connecting...";
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

            var manifest = await _backupService.RunBackupAsync(SelectedAccount, password, workDir, progress);

            if (CompressToZip)
            {
                var zipPath = Path.Combine(DestinationDirectory, $"{SelectedAccount.Username}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                _compressionService.CompressDirectory(workDir, zipPath);
                Directory.Delete(workDir, recursive: true);
                StatusText = $"Backup complete: {manifest.TotalMessages} messages -> {zipPath}";
            }
            else
            {
                StatusText = $"Backup complete: {manifest.TotalMessages} messages in {DestinationDirectory}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Backup failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }
}
