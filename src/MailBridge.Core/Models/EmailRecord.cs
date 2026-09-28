namespace MailBridge.Core.Models;

/// <summary>
/// Metadata captured for a single backed-up message. Stored in the per-folder
/// JSON index and also used as the comparison basis for duplicate detection
/// during restore.
/// </summary>
public sealed class EmailRecord
{
    /// <summary>Relative path of the raw .eml file inside the backup, e.g. "INBOX/000123.eml".</summary>
    public required string RelativeEmlPath { get; set; }

    public required string Folder { get; set; }

    public uint Uid { get; set; }

    /// <summary>Value of the Message-ID header, without angle brackets. May be null for malformed mail.</summary>
    public string? MessageId { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    public string? Subject { get; set; }

    public DateTimeOffset? Date { get; set; }

    public long SizeInBytes { get; set; }

    public List<string> Flags { get; set; } = new();
}
