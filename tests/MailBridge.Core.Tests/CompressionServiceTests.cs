using MailBridge.Core.Services;
using Xunit;

namespace MailBridge.Core.Tests;

public sealed class CompressionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MailBridgeTests", Guid.NewGuid().ToString("N"));
    private readonly CompressionService _service = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void CompressDirectory_PacksNestedFilesAndReportsEachStepUpToTheTotal()
    {
        var source = Path.Combine(_root, "backup");
        Write(source, "manifest.json", "{}");
        Write(source, Path.Combine("INBOX", "00000001.eml"), "Subject: 1");
        Write(source, Path.Combine("Archiv", "2023", "00000002.eml"), "Subject: 2");
        var zip = Path.Combine(_root, "backup.zip");
        var progress = new List<CompressionProgress>();

        _service.CompressDirectory(source, zip, new SyncProgress(progress.Add));
        var extracted = _service.ExtractToTempDirectory(zip);

        try
        {
            Assert.Equal("Subject: 2", File.ReadAllText(Path.Combine(extracted, "Archiv", "2023", "00000002.eml")));
            Assert.Equal(3, Directory.GetFiles(extracted, "*", SearchOption.AllDirectories).Length);
        }
        finally
        {
            Directory.Delete(extracted, recursive: true);
        }

        Assert.Equal(new[] { 0, 1, 2, 3 }, progress.Select(p => p.FilesDone));
        Assert.All(progress, p => Assert.Equal(3, p.FilesTotal));
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class SyncProgress(Action<CompressionProgress> report) : IProgress<CompressionProgress>
    {
        public void Report(CompressionProgress value) => report(value);
    }
}
