using System.IO.Compression;

namespace MailBridge.Core.Services;

/// <summary>Files packed so far into the archive being written.</summary>
public sealed record CompressionProgress(int FilesDone, int FilesTotal);

/// <summary>
/// Packs a folder-based backup into a single .zip archive (or extracts one
/// back to a working folder before restore). Kept separate from
/// <see cref="BackupService"/> so backups can be produced either as a plain
/// folder or compressed, per user choice.
/// </summary>
public sealed class CompressionService
{
    /// <summary>
    /// Packs every file below <paramref name="sourceDirectory"/> (paths relative to it) into a new
    /// archive, file by file so <paramref name="progress"/> can follow. Progress is reported at
    /// most about every 0.5 %, so a backup of many small messages does not flood the UI thread.
    /// </summary>
    public void CompressDirectory(string sourceDirectory, string destinationZipPath, IProgress<CompressionProgress>? progress = null)
    {
        if (File.Exists(destinationZipPath))
        {
            File.Delete(destinationZipPath);
        }

        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
        progress?.Report(new CompressionProgress(0, files.Length));

        try
        {
            using var archive = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create);
            for (var i = 0; i < files.Length; i++)
            {
                var entryName = Path.GetRelativePath(sourceDirectory, files[i]).Replace(Path.DirectorySeparatorChar, '/');
                archive.CreateEntryFromFile(files[i], entryName, CompressionLevel.Optimal);

                var done = i + 1;
                if (progress is not null && (done == files.Length || done * 200L / files.Length != i * 200L / files.Length))
                {
                    progress.Report(new CompressionProgress(done, files.Length));
                }
            }
        }
        catch
        {
            // A half-written archive must not look like a usable backup.
            File.Delete(destinationZipPath);
            throw;
        }
    }

    public string ExtractToTempDirectory(string zipPath)
    {
        var target = Path.Combine(Path.GetTempPath(), "MailBridge", Path.GetFileNameWithoutExtension(zipPath) + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        ZipFile.ExtractToDirectory(zipPath, target);
        return target;
    }
}
