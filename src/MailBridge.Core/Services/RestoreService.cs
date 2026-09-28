using System.Text.Json;
using MailBridge.Core.Models;
using MailKit;
using MimeKit;

namespace MailBridge.Core.Services;

public sealed class RestoreOutcome
{
    public int Restored { get; set; }
    public int Skipped { get; set; }
    public int Replaced { get; set; }
    public bool Aborted { get; set; }
}

/// <summary>
/// Context handed to the caller-supplied conflict callback so the UI can
/// show exactly why something looks like a duplicate/conflict before the
/// user decides.
/// </summary>
public sealed class RestoreConflict
{
    public required EmailRecord Candidate { get; init; }
    public required DuplicateCheckResult DuplicateCheck { get; init; }
}

/// <summary>
/// Restores messages from a backup (produced by <see cref="BackupService"/>)
/// into a target IMAP account, applying an optional filter and running
/// every candidate through <see cref="DuplicateDetector"/> first.
///
/// A confirmed duplicate (all compared attributes identical) is skipped
/// automatically without asking, since restoring it would create a true
/// exact copy. Anything else that shares an identifying attribute but
/// differs in some way is a conflict: the supplied <paramref name="onConflict"/>
/// callback decides Replace / Replace all / Skip / Skip all / Abort. New
/// messages with no match are uploaded directly.
/// </summary>
public sealed class RestoreService
{
    private readonly ImapConnectionService _imap;
    private readonly DuplicateDetector _duplicateDetector = new();

    public RestoreService(ImapConnectionService imap)
    {
        _imap = imap;
    }

    public async Task<RestoreOutcome> RunRestoreAsync(
        string backupDirectory,
        EmailAccount targetAccount,
        string password,
        RestoreFilter filter,
        Func<RestoreConflict, Task<ConflictResolution>> onConflict,
        CancellationToken cancellationToken = default)
    {
        var outcome = new RestoreOutcome();
        var manifest = await LoadManifestAsync(backupDirectory, cancellationToken).ConfigureAwait(false);

        using var client = await _imap.ConnectAsync(targetAccount, password, cancellationToken).ConfigureAwait(false);

        ConflictResolution? stickyDecision = null;

        foreach (var folderName in manifest.Folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var index = await LoadFolderIndexAsync(backupDirectory, folderName, cancellationToken).ConfigureAwait(false);
            var candidates = index.Where(filter.Matches).ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            var targetFolder = await client.GetFolderAsync(folderName, cancellationToken).ConfigureAwait(false)
                               ?? client.Inbox;
            await targetFolder.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);

            var existingSummaries = await targetFolder.FetchAsync(
                await targetFolder.SearchAsync(MailKit.Search.SearchQuery.All, cancellationToken).ConfigureAwait(false),
                MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.Size,
                cancellationToken).ConfigureAwait(false);

            var existingRecords = existingSummaries.Select(s => new EmailRecord
            {
                RelativeEmlPath = string.Empty,
                Folder = folderName,
                Uid = s.UniqueId.Id,
                MessageId = s.Envelope?.MessageId,
                From = s.Envelope?.From?.ToString(),
                To = s.Envelope?.To?.ToString(),
                Subject = s.Envelope?.Subject,
                Date = s.Envelope?.Date,
                SizeInBytes = s.Size ?? 0,
            }).ToList();

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var check = _duplicateDetector.Compare(candidate, existingRecords);

                if (check.Kind == DuplicateMatchKind.ConfirmedDuplicate)
                {
                    outcome.Skipped++;
                    continue;
                }

                var decision = ConflictResolution.Replace;

                if (check.Kind == DuplicateMatchKind.ConflictingMatch)
                {
                    decision = stickyDecision ?? await onConflict(new RestoreConflict { Candidate = candidate, DuplicateCheck = check }).ConfigureAwait(false);

                    if (decision == ConflictResolution.Abort)
                    {
                        outcome.Aborted = true;
                        return outcome;
                    }

                    if (decision is ConflictResolution.ReplaceAll or ConflictResolution.SkipAll)
                    {
                        stickyDecision = decision == ConflictResolution.ReplaceAll ? ConflictResolution.Replace : ConflictResolution.Skip;
                    }

                    if (decision is ConflictResolution.SkipAll or ConflictResolution.Skip)
                    {
                        outcome.Skipped++;
                        continue;
                    }
                }

                var emlPath = Path.Combine(backupDirectory, candidate.RelativeEmlPath);
                var message = await MimeMessage.LoadAsync(emlPath, cancellationToken).ConfigureAwait(false);

                if (check.Kind == DuplicateMatchKind.ConflictingMatch && decision is ConflictResolution.Replace && check.ExistingUid.HasValue)
                {
                    await targetFolder.AddFlagsAsync(new UniqueId(check.ExistingUid.Value), MessageFlags.Deleted, true, cancellationToken).ConfigureAwait(false);
                    await targetFolder.AppendAsync(message, MessageFlags.Seen, cancellationToken).ConfigureAwait(false);
                    outcome.Replaced++;
                }
                else
                {
                    await targetFolder.AppendAsync(message, MessageFlags.None, cancellationToken).ConfigureAwait(false);
                    outcome.Restored++;
                }
            }

            await targetFolder.ExpungeAsync(cancellationToken).ConfigureAwait(false);
            await targetFolder.CloseAsync(false, cancellationToken).ConfigureAwait(false);
        }

        return outcome;
    }

    private static async Task<BackupManifest> LoadManifestAsync(string backupDirectory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(backupDirectory, "manifest.json");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<BackupManifest>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Backup manifest is missing or invalid.");
    }

    private static async Task<List<EmailRecord>> LoadFolderIndexAsync(string backupDirectory, string folderName, CancellationToken cancellationToken)
    {
        var safe = folderName.Split('/', '\\');
        var indexPath = Path.Combine(Path.Combine(backupDirectory, Path.Combine(safe)), "index.json");
        if (!File.Exists(indexPath))
        {
            return new List<EmailRecord>();
        }

        await using var stream = File.OpenRead(indexPath);
        return await JsonSerializer.DeserializeAsync<List<EmailRecord>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
               ?? new List<EmailRecord>();
    }
}
