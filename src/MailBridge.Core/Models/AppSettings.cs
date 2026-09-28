namespace MailBridge.Core.Models;

/// <summary>
/// Application-wide preferences shown on the Settings page. Stored as a
/// single JSON document; secrets never belong here (they live in the
/// OS credential store).
/// </summary>
public sealed class AppSettings
{
    /// <summary>App color scheme: "System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Pre-filled destination folder on the Backup and Schedule pages.</summary>
    public string DefaultBackupDirectory { get; set; } = string.Empty;

    /// <summary>Initial state of the "compress to zip" checkbox.</summary>
    public bool DefaultCompressToZip { get; set; } = true;
}
