namespace MailBridge.Core.Models;

/// <summary>
/// A folder of the target account that a restore writes into instead of the folders named in the
/// backup. Without one, every backed-up folder is restored into the folder of the same name.
/// </summary>
public sealed class RestoreDestination
{
    /// <summary>Full name of the folder on the target account, e.g. "Archiv/Altes Konto".</summary>
    public required string FolderFullName { get; init; }

    /// <summary>Hierarchy separator used in <see cref="FolderFullName"/>; null to assume the account's own.</summary>
    public char? Separator { get; init; }

    /// <summary>
    /// True: the backed-up folders are recreated below <see cref="FolderFullName"/>
    /// ("Archiv/Altes Konto/INBOX", ...). False: all messages go directly into that folder.
    /// </summary>
    public bool KeepFolderStructure { get; init; }
}
