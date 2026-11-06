using MailBridge.Core.Services;
using Xunit;

namespace MailBridge.Core.Tests.Imap;

public class ImapConnectionServiceTests
{
    private readonly ImapConnectionService _service = new();

    [Fact]
    public async Task GetAllFolders_FindsNestedAndUnsubscribedFolders_ButNoPlaceholders()
    {
        await using var server = new FakeImapServer('/', "",
            new FakeFolder("INBOX"),
            new FakeFolder("Projekte", Subscribed: false),
            new FakeFolder("Archiv"),
            new FakeFolder("Archiv/2023", Subscribed: false),
            new FakeFolder("Archiv/2023/Q1"),
            new FakeFolder("INBOX/Rechnungen"),
            new FakeFolder("[Gmail]", NoSelect: true),
            new FakeFolder("[Gmail]/Sent Mail"),
            new FakeFolder("[Gmail]/All Mail", Subscribed: false));
        using var client = await server.ConnectAsync();

        var folders = await _service.GetAllFoldersAsync(client);

        // INBOX and its subfolders first, then the rest with every subfolder right after its parent.
        Assert.Equal(
            new[] { "INBOX", "INBOX/Rechnungen", "[Gmail]/All Mail", "[Gmail]/Sent Mail", "Archiv", "Archiv/2023", "Archiv/2023/Q1", "Projekte" },
            folders.Select(f => f.FullName));
    }

    [Fact]
    public async Task GetAllFolders_FindsFoldersBelowAnInboxNamespacePrefix()
    {
        // Courier/older Dovecot layout: every folder lives below "INBOX." with "." as separator.
        await using var server = new FakeImapServer('.', "INBOX.",
            new FakeFolder("INBOX"),
            new FakeFolder("INBOX.Sent"),
            new FakeFolder("INBOX.Archiv", Subscribed: false),
            new FakeFolder("INBOX.Archiv.2023"));
        using var client = await server.ConnectAsync();

        var folders = await _service.GetAllFoldersAsync(client);

        Assert.Equal(new[] { "INBOX", "INBOX.Archiv", "INBOX.Archiv.2023", "INBOX.Sent" }, folders.Select(f => f.FullName));
    }

    [Fact]
    public async Task GetOrCreateFolder_ReturnsExistingFolderWithoutCreatingAnything()
    {
        await using var server = new FakeImapServer('/', "", new FakeFolder("Archiv"), new FakeFolder("Archiv/2023"));
        using var client = await server.ConnectAsync();

        var folder = await _service.GetOrCreateFolderAsync(client, "Archiv/2023", '/');

        Assert.Equal("Archiv/2023", folder.FullName);
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("CREATE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetOrCreateFolder_CreatesMissingParentsAndFolder()
    {
        await using var server = new FakeImapServer('/', "", new FakeFolder("Archiv"));
        using var client = await server.ConnectAsync();

        var folder = await _service.GetOrCreateFolderAsync(client, "Archiv/2023/Q1", '/');

        Assert.Equal("Archiv/2023/Q1", folder.FullName);
        Assert.Contains("Archiv/2023", server.FolderNames);
        Assert.Contains("Archiv/2023/Q1", server.FolderNames);
        Assert.Equal(2, server.Commands.Count(c => c.StartsWith("CREATE", StringComparison.Ordinal))); // "Archiv" already existed
    }

    [Fact]
    public async Task GetOrCreateFolder_MapsFoldersOfAnotherServerLayout()
    {
        // Backup from a "/" server restored into a server that keeps everything below "INBOX.".
        await using var server = new FakeImapServer('.', "INBOX.", new FakeFolder("INBOX"));
        using var client = await server.ConnectAsync();

        var nested = await _service.GetOrCreateFolderAsync(client, "Projekte/Alt", '/');
        var belowInbox = await _service.GetOrCreateFolderAsync(client, "INBOX/Rechnungen", '/');
        var inbox = await _service.GetOrCreateFolderAsync(client, "INBOX", '/');

        Assert.Equal("INBOX.Projekte.Alt", nested.FullName);
        Assert.Equal("INBOX.Rechnungen", belowInbox.FullName);
        Assert.Same(client.Inbox, inbox);
        Assert.Contains("INBOX.Projekte", server.FolderNames);
    }

    [Fact]
    public async Task GetOrCreateFolder_WithoutKnownSeparator_UsesTheServersOwn()
    {
        // Backups written before the separator was recorded.
        await using var server = new FakeImapServer('/', "");
        using var client = await server.ConnectAsync();

        var folder = await _service.GetOrCreateFolderAsync(client, "Archiv/2023", separator: null);

        Assert.Equal("Archiv/2023", folder.FullName);
        Assert.Contains("Archiv", server.FolderNames);
    }
}
