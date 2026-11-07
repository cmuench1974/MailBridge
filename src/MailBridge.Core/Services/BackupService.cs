using System.Text.Json;
using MailBridge.Core.Models;
using MailKit;

namespace MailBridge.Core.Services;

public enum BackupPhase
{
    Connecting,

    /// <summary>Message counts of the folders to back up are fetched (IMAP STATUS).</summary>
    Counting,

    Downloading,
}

/// <summary>
/// Progress of a backup run. The message counts of all folders are fetched before the first
/// download, so <see cref="TotalMessages"/> covers the whole run; it is corrected as each folder
/// is opened, in case mail arrived or was deleted in the meantime.
/// </summary>
public sealed class BackupProgress
{
    public BackupPhase Phase { get; init; }

    /// <summary>Folder being downloaded; empty before the first one.</summary>
    public string Folder { get; init; } = string.Empty;

    /// <summary>1-based position of <see cref="Folder"/> among the folders to back up.</summary>
    public int FolderNumber { get; init; }

    public int FolderCount { get; init; }

    /// <summary>Messages downloaded so far in <see cref="Folder"/>.</summary>
    public int MessagesDone { get; init; }

    /// <summary>Messages to download in <see cref="Folder"/>.</summary>
    public int MessagesTotal { get; init; }

    /// <summary>Messages downloaded so far in the whole run.</summary>
    public int TotalMessagesDone { get; init; }

    /// <summary>Messages to download in the whole run.</summary>
    public int TotalMessages { get; init; }
}

/// <summary>
/// Downloads every folder/message of an IMAP account to a local directory:
/// one raw .eml file per message plus a per-folder JSON index, and a root
/// manifest.json. Compression into a single archive is a separate,
/// opt-in step (see <see cref="CompressionService"/>) so a backup can be
/// inspected as plain files if desired.
/// </summary>
public sealed class BackupService
{
    private readonly ImapConnectionService _imap;

    public BackupService(ImapConnectionService imap)
    {
        _imap = imap;
    }

    public async Task<BackupManifest> RunBackupAsync(
        EmailAccount account,
        ImapCredential credential,
        string destinationDirectory,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default,
        BackupSelection? selection = null)
    {
        Directory.CreateDirectory(destinationDirectory);

        var manifest = new BackupManifest
        {
            SourceHost = account.Host,
            SourceUsername = account.Username,
        };

        progress?.Report(new BackupProgress { Phase = BackupPhase.Connecting });
        using var client = await _imap.ConnectAsync(account, credential, cancellationToken).ConfigureAwait(false);
        var folders = await _imap.GetAllFoldersAsync(client, cancellationToken).ConfigureAwait(false);

        // Folders to back up with their message counts, so progress can cover the whole run.
        progress?.Report(new BackupProgress { Phase = BackupPhase.Counting });
        var plan = new List<(IMailFolder Folder, FolderSelection? Selection, int Count)>();
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(folder.FullName) || folder.Attributes.HasFlag(FolderAttributes.NoSelect))
            {
                continue;
            }

            FolderSelection? folderSelection = null;
            if (selection is { IsEmpty: false })
            {
                if (!selection.Folders.TryGetValue(folder.FullName, out folderSelection) || !folderSelection.IncludeFolder)
                {
                    continue;
                }
            }

            var count = folderSelection?.IncludedItems?.Count
                        ?? await CountMessagesAsync(folder, cancellationToken).ConfigureAwait(false);
            plan.Add((folder, folderSelection, count));
        }

        var totalMessages = plan.Sum(p => p.Count);
        var totalDone = 0;

        for (var folderIndex = 0; folderIndex < plan.Count; folderIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (folder, folderSelection, expectedCount) = plan[folderIndex];

            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
            var uids = await folder.SearchAsync(MailKit.Search.SearchQuery.All, cancellationToken).ConfigureAwait(false);

            if (folderSelection?.IncludedItems is { } wanted)
            {
                uids = uids.Where(uid => wanted.Contains(uid.Id.ToString())).ToList();
            }

            totalMessages += uids.Count - expectedCount;
            ReportFolder(0);

            if (folderSelection?.IncludedItems is not null && uids.Count == 0)
            {
                await folder.CloseAsync(false, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var safeFolderName = FolderDirectory(folder.FullName);
            var folderDir = Path.Combine(destinationDirectory, safeFolderName);
            Directory.CreateDirectory(folderDir);

            var records = new List<EmailRecord>();
            var count = 0;

            foreach (var uid in uids)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var message = await folder.GetMessageAsync(uid, cancellationToken).ConfigureAwait(false);
                var summary = (await folder.FetchAsync(new[] { uid }, MessageSummaryItems.Flags | MessageSummaryItems.Size, cancellationToken).ConfigureAwait(false)).FirstOrDefault();

                var fileName = $"{uid.Id:D8}.eml";
                var fullPath = Path.Combine(folderDir, fileName);
                await using (var stream = File.Create(fullPath))
                {
                    await message.WriteToAsync(stream, cancellationToken).ConfigureAwait(false);
                }

                records.Add(new EmailRecord
                {
                    RelativeEmlPath = Path.Combine(safeFolderName, fileName).Replace('\\', '/'),
                    Folder = folder.FullName,
                    Uid = uid.Id,
                    MessageId = message.MessageId,
                    From = message.From?.ToString(),
                    To = message.To?.ToString(),
                    Subject = message.Subject,
                    Date = message.Date,
                    SizeInBytes = new FileInfo(fullPath).Length,
                    Flags = summary?.Flags?.ToString().Split(',').Select(f => f.Trim()).ToList() ?? new List<string>(),
                });

                count++;
                totalDone++;
                ReportFolder(count);
            }

            var indexPath = Path.Combine(folderDir, "index.json");
            await using (var indexStream = File.Create(indexPath))
            {
                await JsonSerializer.SerializeAsync(indexStream, records, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            manifest.Folders.Add(folder.FullName);
            if (folder.DirectorySeparator != '\0')
            {
                manifest.FolderSeparator ??= folder.DirectorySeparator;
            }

            manifest.TotalMessages += records.Count;
            manifest.TotalSizeInBytes += records.Sum(r => r.SizeInBytes);

            await folder.CloseAsync(false, cancellationToken).ConfigureAwait(false);

            void ReportFolder(int done) => progress?.Report(new BackupProgress
            {
                Phase = BackupPhase.Downloading,
                Folder = folder.FullName,
                FolderNumber = folderIndex + 1,
                FolderCount = plan.Count,
                MessagesDone = done,
                MessagesTotal = uids.Count,
                TotalMessagesDone = totalDone,
                TotalMessages = totalMessages,
            });
        }

        var manifestPath = Path.Combine(destinationDirectory, "manifest.json");
        await using (var manifestStream = File.Create(manifestPath))
        {
            await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        return manifest;
    }

    /// <summary>
    /// Message count of a folder via STATUS, without selecting it. Only used for progress: 0 if the
    /// server refuses, the real count follows when the folder is opened.
    /// </summary>
    private static async Task<int> CountMessagesAsync(IMailFolder folder, CancellationToken cancellationToken)
    {
        try
        {
            await folder.StatusAsync(StatusItems.Count, cancellationToken).ConfigureAwait(false);
            return folder.Count;
        }
        catch (Exception ex) when (ex is MailKit.Net.Imap.ImapCommandException or NotSupportedException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Relative directory of a folder inside a backup: one directory level per "/" or "\" in the
    /// name, characters Windows does not allow replaced by "_", and "." or ".." levels defused so
    /// a folder name can never point outside the backup. Backup and restore must agree on it.
    /// </summary>
    internal static string FolderDirectory(string folderFullName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var parts = folderFullName.Split('/', '\\')
            .Select(p => new string(p.Select(c => invalid.Contains(c) ? '_' : c).ToArray()))
            .Select(p => p.Length > 0 && p.All(c => c == '.') ? new string('_', p.Length) : p);
        return Path.Combine(parts.ToArray());
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
