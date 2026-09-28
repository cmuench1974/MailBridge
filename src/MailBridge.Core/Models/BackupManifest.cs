namespace MailBridge.Core.Models;

/// <summary>
/// Top-level manifest written at the root of every backup (folder or zip).
/// One <see cref="EmailRecord"/> index file exists per IMAP folder; this
/// manifest lists which folders were captured plus run metadata.
/// </summary>
public sealed class BackupManifest
{
    public int FormatVersion { get; set; } = 1;

    public required string SourceHost { get; set; }

    public required string SourceUsername { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<string> Folders { get; set; } = new();

    public int TotalMessages { get; set; }

    public long TotalSizeInBytes { get; set; }
}
