using MailBridge.Core.Models;
using MailBridge.Core.Services;
using Xunit;

namespace MailBridge.Core.Tests.Imap;

/// <summary>Whole backup and restore runs against <see cref="FakeImapServer"/>, including the progress they report.</summary>
public sealed class BackupRestoreRunTests : IDisposable
{
    private readonly string _backup = Path.Combine(Path.GetTempPath(), "MailBridgeTests", Guid.NewGuid().ToString("N"));
    private readonly ImapConnectionService _imap = new() { UnencryptedForTests = true };

    public void Dispose()
    {
        if (Directory.Exists(_backup))
        {
            Directory.Delete(_backup, recursive: true);
        }
    }

    [Fact]
    public async Task Backup_ReportsProgressAgainstTheTotalOfAllFolders()
    {
        await using var source = await SourceServerAsync();
        var progress = new Collector<BackupProgress>();

        var manifest = await new BackupService(_imap).RunBackupAsync(Account(source), Password, _backup, progress);

        Assert.Equal(3, manifest.TotalMessages);
        Assert.Equal(new[] { "INBOX", "Archiv" }, manifest.Folders);

        Assert.Equal(BackupPhase.Connecting, progress.Items[0].Phase);
        Assert.Equal(BackupPhase.Counting, progress.Items[1].Phase);
        var downloads = progress.Items.Where(p => p.Phase == BackupPhase.Downloading).ToList();
        Assert.All(downloads, p => Assert.Equal(3, p.TotalMessages)); // counted before the first download
        Assert.All(downloads, p => Assert.Equal(2, p.FolderCount));
        Assert.Equal(new[] { 0, 1, 2, 2, 3 }, downloads.Select(p => p.TotalMessagesDone));
        Assert.Equal(new[] { 1, 1, 1, 2, 2 }, downloads.Select(p => p.FolderNumber));

        var last = downloads[^1];
        Assert.Equal(("Archiv", 1, 1), (last.Folder, last.MessagesDone, last.MessagesTotal));
    }

    [Fact]
    public async Task Restore_IntoSameNamedFolders_ReportsProgressAndSubscribesCreatedFolders()
    {
        await BackUpSourceAsync();
        await using var target = new FakeImapServer('/', "");
        var progress = new Collector<RestoreProgress>();

        var outcome = await RestoreAsync(target, destination: null, progress);

        Assert.Equal((3, 0, 0), (outcome.Restored, outcome.Replaced, outcome.Skipped));
        Assert.Equal(2, target.MessagesIn("INBOX").Count);
        Assert.Single(target.MessagesIn("Archiv"));
        Assert.Contains("Archiv", target.SubscribedFolderNames);

        Assert.Equal(RestorePhase.Connecting, progress.Items[0].Phase);
        Assert.All(progress.Items, p => Assert.Equal(3, p.MessagesTotal)); // known from the index files up front
        Assert.Contains(progress.Items, p => p is { Phase: RestorePhase.CheckingFolder, TargetFolder: "Archiv", FolderNumber: 2, FolderCount: 2 });
        var last = progress.Items[^1];
        Assert.Equal((RestorePhase.Restoring, 3, 3), (last.Phase, last.MessagesDone, last.Restored));
    }

    [Fact]
    public async Task Restore_BelowChosenFolder_RecreatesTheBackupTree()
    {
        await BackUpSourceAsync();
        await using var target = new FakeImapServer('.', "INBOX.", new FakeFolder("INBOX.Altes Konto"));
        var progress = new Collector<RestoreProgress>();

        var outcome = await RestoreAsync(target, new RestoreDestination { FolderFullName = "INBOX.Altes Konto", Separator = '.', KeepFolderStructure = true }, progress);

        Assert.Equal(3, outcome.Restored);
        Assert.Equal(2, target.MessagesIn("INBOX.Altes Konto.INBOX").Count);
        Assert.Single(target.MessagesIn("INBOX.Altes Konto.Archiv"));
        Assert.Empty(target.MessagesIn("INBOX"));
        Assert.Contains("INBOX.Altes Konto.INBOX", target.SubscribedFolderNames);
        Assert.Contains(progress.Items, p => p is { Folder: "INBOX", TargetFolder: "INBOX.Altes Konto.INBOX" });
    }

    [Fact]
    public async Task Restore_IntoChosenFolder_PutsEverythingDirectlyIntoIt()
    {
        await BackUpSourceAsync();
        await using var target = new FakeImapServer('/', "", new FakeFolder("Wiederhergestellt", Subscribed: false));

        var outcome = await RestoreAsync(target, new RestoreDestination { FolderFullName = "Wiederhergestellt", Separator = '/' }, progress: null);

        Assert.Equal(3, outcome.Restored);
        Assert.Equal(3, target.MessagesIn("Wiederhergestellt").Count);
        Assert.DoesNotContain(target.FolderNames, f => f.StartsWith("Wiederhergestellt/", StringComparison.Ordinal));
        Assert.Contains("Wiederhergestellt", target.SubscribedFolderNames); // existing, but restored mail must be visible
    }

    private static readonly ImapCredential Password = ImapCredential.FromPassword("secret");

    private static EmailAccount Account(FakeImapServer server) =>
        new() { Host = "127.0.0.1", Port = server.Port, Username = "user", UseSsl = false };

    private static async Task<FakeImapServer> SourceServerAsync()
    {
        var server = new FakeImapServer('/', "", new FakeFolder("INBOX"), new FakeFolder("Archiv"));
        server.AddMessage("INBOX", Message(1, "Rechnung"), "\\Seen");
        server.AddMessage("INBOX", Message(2, "Einladung"));
        server.AddMessage("Archiv", Message(3, "Alt"), "\\Seen");
        await Task.CompletedTask;
        return server;
    }

    private async Task BackUpSourceAsync()
    {
        await using var source = await SourceServerAsync();
        await new BackupService(_imap).RunBackupAsync(Account(source), Password, _backup);
    }

    private Task<RestoreOutcome> RestoreAsync(FakeImapServer target, RestoreDestination? destination, IProgress<RestoreProgress>? progress) =>
        new RestoreService(_imap).RunRestoreAsync(
            _backup,
            Account(target),
            Password,
            new RestoreFilter(),
            _ => throw new InvalidOperationException("No conflicts expected in an empty target."),
            destination: destination,
            progress: progress);

    private static string Message(int number, string subject) =>
        $"From: Absender <absender@example.com>\r\n" +
        $"To: empfaenger@example.com\r\n" +
        $"Subject: {subject}\r\n" +
        $"Date: Mon, 02 Nov 2026 10:0{number}:00 +0100\r\n" +
        $"Message-ID: <{number}@example.com>\r\n" +
        $"\r\n" +
        $"Nachricht {number}\r\n";

    /// <summary>Records reports synchronously, unlike <see cref="Progress{T}"/>.</summary>
    private sealed class Collector<T> : IProgress<T>
    {
        public List<T> Items { get; } = new();

        public void Report(T value)
        {
            lock (Items)
            {
                Items.Add(value);
            }
        }
    }
}
