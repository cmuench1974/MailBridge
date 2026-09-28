namespace MailBridge.Core.Models;

/// <summary>
/// Per-folder selection state. <see cref="IncludedItems"/> being null means
/// "all messages of this folder"; otherwise it contains exactly the items to
/// process. The item key depends on the side:
/// <list type="bullet">
/// <item>backup: IMAP UID as string (server side)</item>
/// <item>restore: <see cref="EmailRecord.RelativeEmlPath"/> (archive side)</item>
/// </list>
/// </summary>
public sealed class FolderSelection
{
    public bool IncludeFolder { get; set; } = true;

    public HashSet<string>? IncludedItems { get; set; }
}

/// <summary>
/// Explicit user selection of folders and individual messages. An absent or
/// empty selection means "everything" (whole account for backup, whole
/// archive for restore), preserving the previous behavior of both tasks.
/// </summary>
public sealed class BackupSelection
{
    public Dictionary<string, FolderSelection> Folders { get; set; } = new();

    public bool IsEmpty => Folders.Count == 0;
}

/// <summary>
/// Everything a restore needs to know about a backup's content: the manifest
/// plus, per folder, the indexed messages (folders absent from the dictionary
/// simply have no index / no messages).
/// </summary>
public sealed class BackupContents
{
    public required BackupManifest Manifest { get; init; }

    public required string Directory { get; init; }

    public Dictionary<string, List<EmailRecord>> Folders { get; } = new();

    public int TotalMessages => Folders.Values.Sum(f => f.Count);
}
