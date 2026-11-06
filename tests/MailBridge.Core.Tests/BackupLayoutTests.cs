using System.Text.Json;
using MailBridge.Core.Models;
using MailBridge.Core.Services;
using Xunit;

namespace MailBridge.Core.Tests;

public sealed class BackupLayoutTests : IDisposable
{
    private readonly string _backup = Path.Combine(Path.GetTempPath(), "MailBridgeTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_backup))
        {
            Directory.Delete(_backup, recursive: true);
        }
    }

    [Fact]
    public async Task GetBackupContents_ReadsSubfoldersWhoseNamesWindowsDoesNotAllow()
    {
        // Directory layout exactly as BackupService writes it ("?" and ":" become "_").
        await WriteBackupAsync(new BackupManifest
        {
            SourceHost = "imap.example.com",
            SourceUsername = "me@example.com",
            Folders = { "INBOX", "Archiv/2023", "Fragen?/Kunde: Alt" },
            FolderSeparator = '/',
        }, new Dictionary<string, string>
        {
            ["INBOX"] = "INBOX",
            ["Archiv/2023"] = Path.Combine("Archiv", "2023"),
            ["Fragen?/Kunde: Alt"] = Path.Combine("Fragen_", "Kunde_ Alt"),
        });

        var contents = await new RestoreService(new ImapConnectionService()).GetBackupContentsAsync(_backup);

        Assert.Equal('/', contents.Manifest.FolderSeparator);
        Assert.All(contents.Manifest.Folders, folder => Assert.Equal(folder, Assert.Single(contents.Folders[folder]).Folder));
    }

    [Fact]
    public async Task GetBackupContents_OlderManifestWithoutSeparator_StillLoads()
    {
        Directory.CreateDirectory(_backup);
        await File.WriteAllTextAsync(Path.Combine(_backup, "manifest.json"),
            """{ "FormatVersion": 1, "SourceHost": "h", "SourceUsername": "u", "Folders": [], "TotalMessages": 0, "TotalSizeInBytes": 0 }""");

        var contents = await new RestoreService(new ImapConnectionService()).GetBackupContentsAsync(_backup);

        Assert.Null(contents.Manifest.FolderSeparator);
    }

    [Theory]
    [InlineData("Archiv/2023/Q1", "Archiv|2023|Q1")]
    [InlineData("INBOX.Archiv.2023", "INBOX.Archiv.2023")]
    [InlineData("Fragen?/Kunde: Alt", "Fragen_|Kunde_ Alt")]
    [InlineData("../../Windows", "__|__|Windows")]
    [InlineData("./x", "_|x")]
    public void FolderDirectory_MapsFolderNamesToSafeRelativePaths(string folder, string expected)
    {
        var path = BackupService.FolderDirectory(folder);

        Assert.Equal(expected.Replace('|', Path.DirectorySeparatorChar), path);
        Assert.False(Path.IsPathRooted(path));
    }

    private async Task WriteBackupAsync(BackupManifest manifest, Dictionary<string, string> directories)
    {
        Directory.CreateDirectory(_backup);
        await File.WriteAllTextAsync(Path.Combine(_backup, "manifest.json"), JsonSerializer.Serialize(manifest));
        foreach (var (folder, directory) in directories)
        {
            var full = Path.Combine(_backup, directory);
            Directory.CreateDirectory(full);
            var record = new EmailRecord { Folder = folder, RelativeEmlPath = Path.Combine(directory, "00000001.eml").Replace('\\', '/'), Uid = 1 };
            await File.WriteAllTextAsync(Path.Combine(full, "index.json"), JsonSerializer.Serialize(new[] { record }));
        }
    }
}
