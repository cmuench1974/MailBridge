namespace MailBridge.Core.Models;

/// <summary>
/// User decision for a single detected conflict during restore.
/// "All" variants are applied by the caller to every remaining conflict
/// in the current restore run, not persisted beyond it.
/// </summary>
public enum ConflictResolution
{
    Replace,
    ReplaceAll,
    Skip,
    SkipAll,
    Abort,
}

/// <summary>
/// Outcome of comparing a backed-up message against an existing server message.
/// </summary>
public enum DuplicateMatchKind
{
    /// <summary>No existing message shares any strong identifying attribute.</summary>
    NoMatch,

    /// <summary>
    /// An existing message shares identifying attributes (Message-ID and/or
    /// From+Date+Subject+Size) AND the compared attributes are fully identical.
    /// Safe to treat as a true duplicate.
    /// </summary>
    ConfirmedDuplicate,

    /// <summary>
    /// An existing message shares some identifying attribute (e.g. same
    /// Message-ID) but other compared attributes differ (e.g. size, flags,
    /// or edited content). Must never be silently skipped - requires a
    /// user decision via <see cref="ConflictResolution"/>.
    /// </summary>
    ConflictingMatch,
}

public sealed class DuplicateCheckResult
{
    public required DuplicateMatchKind Kind { get; init; }

    public uint? ExistingUid { get; init; }

    public List<string> MatchedAttributes { get; init; } = new();

    public List<string> DifferingAttributes { get; init; } = new();
}
