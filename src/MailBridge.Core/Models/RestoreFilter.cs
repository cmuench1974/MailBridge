namespace MailBridge.Core.Models;

/// <summary>
/// User-selectable criteria to narrow down which backed-up messages are
/// actually restored. All set fields are combined with AND.
/// </summary>
public sealed class RestoreFilter
{
    /// <summary>Restrict to these source folders (backup folder names). Empty = all folders.</summary>
    public HashSet<string> IncludeFolders { get; set; } = new();

    public DateTimeOffset? DateFrom { get; set; }

    public DateTimeOffset? DateTo { get; set; }

    /// <summary>Case-insensitive substring match against From header.</summary>
    public string? FromContains { get; set; }

    /// <summary>Case-insensitive substring match against Subject header.</summary>
    public string? SubjectContains { get; set; }

    public bool Matches(EmailRecord record)
    {
        if (IncludeFolders.Count > 0 && !IncludeFolders.Contains(record.Folder))
        {
            return false;
        }

        if (DateFrom.HasValue && (record.Date is null || record.Date < DateFrom))
        {
            return false;
        }

        if (DateTo.HasValue && (record.Date is null || record.Date > DateTo))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(FromContains) &&
            (record.From is null || !record.From.Contains(FromContains, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(SubjectContains) &&
            (record.Subject is null || !record.Subject.Contains(SubjectContains, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }
}
