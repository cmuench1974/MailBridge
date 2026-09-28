using System.Text.Json;
using MailBridge.Core.Models;
using MailKit;

namespace MailBridge.Core.Services;

public sealed class BackupProgress
{
    public required string Folder { get; init; }
    public int MessagesDone { get; init; }
    public int MessagesTotal { get; init; }
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
        string password,
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

        using var client = await _imap.ConnectAsync(account, password, cancellationToken).ConfigureAwait(false);
        var folders = await _imap.GetAllFoldersAsync(client, cancellationToken).ConfigureAwait(false);

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

            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
            var uids = await folder.SearchAsync(MailKit.Search.SearchQuery.All, cancellationToken).ConfigureAwait(false);

            if (folderSelection?.IncludedItems is { } wanted)
            {
                uids = uids.Where(uid => wanted.Contains(uid.Id.ToString())).ToList();
                if (uids.Count == 0)
                {
                    await folder.CloseAsync(false, cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            var safeFolderName = SanitizeForPath(folder.FullName);
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
                progress?.Report(new BackupProgress { Folder = folder.FullName, MessagesDone = count, MessagesTotal = uids.Count });
            }

            var indexPath = Path.Combine(folderDir, "index.json");
            await using (var indexStream = File.Create(indexPath))
            {
                await JsonSerializer.SerializeAsync(indexStream, records, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            manifest.Folders.Add(folder.FullName);
            manifest.TotalMessages += records.Count;
            manifest.TotalSizeInBytes += records.Sum(r => r.SizeInBytes);

            await folder.CloseAsync(false, cancellationToken).ConfigureAwait(false);
        }

        var manifestPath = Path.Combine(destinationDirectory, "manifest.json");
        await using (var manifestStream = File.Create(manifestPath))
        {
            await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        return manifest;
    }

    private static string SanitizeForPath(string folderFullName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var parts = folderFullName.Split('/', '\\').Select(p => new string(p.Select(c => invalid.Contains(c) ? '_' : c).ToArray()));
        return Path.Combine(parts.ToArray());
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
