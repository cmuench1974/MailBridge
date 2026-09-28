using MailBridge.Core.Models;

namespace MailBridge.Core.Services;

/// <summary>
/// Compares a candidate message (from a backup) against messages already
/// present on the target server to decide whether it is a true duplicate,
/// a conflicting look-alike, or genuinely new.
///
/// Strategy: Message-ID is the strongest signal (it is meant to be globally
/// unique per RFC 5322). If a server message with the same Message-ID
/// exists, we then compare From, Date, Subject and size as corroborating
/// attributes. Only when ALL compared attributes agree do we call it a
/// confirmed duplicate; any disagreement is surfaced as a conflict so the
/// user decides, rather than risking data loss by assuming equality.
///
/// If no Message-ID is present on either side (rare, malformed mail), we
/// fall back to comparing From + Date + Subject + size together as a
/// combined identifying key.
/// </summary>
public sealed class DuplicateDetector
{
    public DuplicateCheckResult Compare(EmailRecord candidate, IReadOnlyList<EmailRecord> existingOnServer)
    {
        var byMessageId = !string.IsNullOrWhiteSpace(candidate.MessageId)
            ? existingOnServer.FirstOrDefault(e => string.Equals(e.MessageId, candidate.MessageId, StringComparison.OrdinalIgnoreCase))
            : null;

        EmailRecord? match = byMessageId;
        var matchedByMessageId = byMessageId != null;

        if (match is null)
        {
            // Fallback identifying key when no Message-ID is available on either side.
            match = existingOnServer.FirstOrDefault(e =>
                string.IsNullOrWhiteSpace(e.MessageId) &&
                string.IsNullOrWhiteSpace(candidate.MessageId) &&
                string.Equals(e.From, candidate.From, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Subject, candidate.Subject, StringComparison.OrdinalIgnoreCase) &&
                e.Date == candidate.Date);
        }

        if (match is null)
        {
            return new DuplicateCheckResult { Kind = DuplicateMatchKind.NoMatch };
        }

        var matched = new List<string>();
        var differing = new List<string>();

        if (matchedByMessageId)
        {
            matched.Add(nameof(EmailRecord.MessageId));
        }

        Compare(nameof(EmailRecord.From), candidate.From, match.From, matched, differing);
        Compare(nameof(EmailRecord.Subject), candidate.Subject, match.Subject, matched, differing);
        Compare(nameof(EmailRecord.Date), candidate.Date?.ToString("O"), match.Date?.ToString("O"), matched, differing);
        Compare(nameof(EmailRecord.SizeInBytes), candidate.SizeInBytes.ToString(), match.SizeInBytes.ToString(), matched, differing);

        var kind = differing.Count == 0
            ? DuplicateMatchKind.ConfirmedDuplicate
            : DuplicateMatchKind.ConflictingMatch;

        return new DuplicateCheckResult
        {
            Kind = kind,
            ExistingUid = match.Uid,
            MatchedAttributes = matched,
            DifferingAttributes = differing,
        };
    }

    private static void Compare(string name, string? candidateValue, string? existingValue, List<string> matched, List<string> differing)
    {
        if (string.Equals(candidateValue, existingValue, StringComparison.OrdinalIgnoreCase))
        {
            matched.Add(name);
        }
        else
        {
            differing.Add(name);
        }
    }
}
