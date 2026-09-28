using MailBridge.Core.Models;
using MailBridge.Core.Services;
using Xunit;

namespace MailBridge.Core.Tests;

public class DuplicateDetectorTests
{
    private static EmailRecord MakeRecord(string? messageId, string? from = "a@example.com", string? subject = "Hi", long size = 100, DateTimeOffset? date = null)
        => new()
        {
            RelativeEmlPath = "INBOX/1.eml",
            Folder = "INBOX",
            Uid = 1,
            MessageId = messageId,
            From = from,
            Subject = subject,
            SizeInBytes = size,
            Date = date ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

    [Fact]
    public void NoMatch_WhenNoSharedIdentifyingAttribute()
    {
        var detector = new DuplicateDetector();
        var candidate = MakeRecord("<abc@x>");
        var existing = new List<EmailRecord> { MakeRecord("<different@x>") };

        var result = detector.Compare(candidate, existing);

        Assert.Equal(DuplicateMatchKind.NoMatch, result.Kind);
    }

    [Fact]
    public void ConfirmedDuplicate_WhenMessageIdAndAllAttributesMatch()
    {
        var detector = new DuplicateDetector();
        var candidate = MakeRecord("<abc@x>");
        var existing = new List<EmailRecord> { MakeRecord("<abc@x>") };

        var result = detector.Compare(candidate, existing);

        Assert.Equal(DuplicateMatchKind.ConfirmedDuplicate, result.Kind);
        Assert.Contains(nameof(EmailRecord.MessageId), result.MatchedAttributes);
    }

    [Fact]
    public void ConflictingMatch_WhenMessageIdMatchesButSizeDiffers()
    {
        var detector = new DuplicateDetector();
        var candidate = MakeRecord("<abc@x>", size: 100);
        var existing = new List<EmailRecord> { MakeRecord("<abc@x>", size: 999) };

        var result = detector.Compare(candidate, existing);

        Assert.Equal(DuplicateMatchKind.ConflictingMatch, result.Kind);
        Assert.Contains(nameof(EmailRecord.SizeInBytes), result.DifferingAttributes);
    }

    [Fact]
    public void RestoreFilter_ExcludesMessagesOutsideDateRange()
    {
        var filter = new RestoreFilter
        {
            DateFrom = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var record = MakeRecord("<abc@x>", date: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.False(filter.Matches(record));
    }
}
