namespace MailBridge.Core.Models;

public enum ScheduleFrequency
{
    Daily,
    Weekly,
}

/// <summary>
/// A recurring backup job for one account. The actual timer is the Windows
/// Task Scheduler (see the App project's task-scheduler service); this
/// record is the persisted description of what to run, plus the outcome of
/// the last run so the UI can show history without needing the app open at
/// the time it ran.
/// </summary>
public sealed class BackupSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required Guid AccountId { get; set; }

    /// <summary>Kept for display purposes even if the account is later removed.</summary>
    public string AccountDisplayName { get; set; } = string.Empty;

    public required string DestinationDirectory { get; set; }

    public bool CompressToZip { get; set; } = true;

    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;

    /// <summary>Only used when Frequency is Weekly.</summary>
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Sunday;

    /// <summary>Local time of day the backup should run.</summary>
    public TimeSpan TimeOfDay { get; set; } = new(2, 0, 0);

    public bool Enabled { get; set; } = true;

    public DateTimeOffset? LastRunUtc { get; set; }

    public bool? LastRunSucceeded { get; set; }

    public string? LastRunMessage { get; set; }
}
