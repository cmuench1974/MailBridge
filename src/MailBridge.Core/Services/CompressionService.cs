using System.IO.Compression;

namespace MailBridge.Core.Services;

/// <summary>
/// Packs a folder-based backup into a single .zip archive (or extracts one
/// back to a working folder before restore). Kept separate from
/// <see cref="BackupService"/> so backups can be produced either as a plain
/// folder or compressed, per user choice.
/// </summary>
public sealed class CompressionService
{
    public void CompressDirectory(string sourceDirectory, string destinationZipPath, IProgress<string>? progress = null)
    {
        if (File.Exists(destinationZipPath))
        {
            File.Delete(destinationZipPath);
        }

        progress?.Report($"Compressing backup into {Path.GetFileName(destinationZipPath)}...");
        ZipFile.CreateFromDirectory(sourceDirectory, destinationZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    public string ExtractToTempDirectory(string zipPath)
    {
        var target = Path.Combine(Path.GetTempPath(), "MailBridge", Path.GetFileNameWithoutExtension(zipPath) + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        ZipFile.ExtractToDirectory(zipPath, target);
        return target;
    }
}
