using MailBridge.App.Localization;

namespace MailBridge.App.ViewModels;

/// <summary>
/// One entry of the restore target folder list: a folder of the target account, or - with
/// <see cref="FullName"/> null - "the folders named in the backup". That entry's name is resolved
/// on read: the view model outlives the window that gets rebuilt when the UI language changes.
/// </summary>
public sealed class TargetFolderOption
{
    public static TargetFolderOption SameAsBackup { get; } = new(null, '\0');

    public TargetFolderOption(string? fullName, char separator)
    {
        FullName = fullName;
        Separator = separator;
    }

    /// <summary>Full folder name on the target account; null for "same folders as in the backup".</summary>
    public string? FullName { get; }

    /// <summary>The account's hierarchy separator for <see cref="FullName"/>.</summary>
    public char Separator { get; }

    public string Name => FullName ?? Strings.Get("restore.targetFolderSameAsBackup");

    public override string ToString() => Name;
}
