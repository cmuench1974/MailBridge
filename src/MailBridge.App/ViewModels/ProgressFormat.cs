using MailBridge.App.Localization;

namespace MailBridge.App.ViewModels;

/// <summary>Percentages and "x of y" lines for the backup and restore progress display.</summary>
internal static class ProgressFormat
{
    public static double Percent(int done, int total) =>
        total > 0 ? Math.Clamp(done * 100.0 / total, 0, 100) : 0;

    /// <summary>"1,234 of 5,678 messages (21 %)".</summary>
    public static string Messages(int done, int total) =>
        Strings.Get("progress.messages", done, total, (int)Percent(done, total));

    /// <summary>"120 of 4,000 files (3 %)".</summary>
    public static string Files(int done, int total) =>
        Strings.Get("progress.files", done, total, (int)Percent(done, total));
}
