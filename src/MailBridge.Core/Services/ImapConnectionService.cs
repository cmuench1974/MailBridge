using MailBridge.Core.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace MailBridge.Core.Services;

/// <summary>
/// Opens and authenticates IMAP connections, with a password or - for Outlook.com and
/// Microsoft 365 - an OAuth2 access token (SASL XOAUTH2). The credential comes from
/// <see cref="AccountCredentialProvider"/>.
/// </summary>
public sealed class ImapConnectionService
{
    public async Task<ImapClient> ConnectAsync(EmailAccount account, ImapCredential credential, CancellationToken cancellationToken = default)
    {
        var client = new ImapClient();
        try
        {
            await client.ConnectAsync(account.Host, account.Port,
                account.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
                cancellationToken).ConfigureAwait(false);

            if (credential.IsOAuth2)
            {
                await client.AuthenticateAsync(new SaslMechanismOAuth2(account.Username, credential.Secret), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await client.AuthenticateAsync(account.Username, credential.Secret, cancellationToken).ConfigureAwait(false);
            }

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Every folder of the account's personal namespaces that can hold messages - at any depth and
    /// whether subscribed or not. INBOX and its subfolders come first, then the rest, with each
    /// subfolder right after its parent. Pure container folders (\Noselect, e.g. Gmail's "[Gmail]")
    /// are left out, but not their subfolders.
    /// </summary>
    public async Task<IReadOnlyList<IMailFolder>> GetAllFoldersAsync(ImapClient client, CancellationToken cancellationToken = default)
    {
        var folders = new List<IMailFolder>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (client.Inbox is { } inbox && IsSelectable(inbox) && seen.Add(inbox.FullName))
        {
            folders.Add(inbox);
        }

        foreach (var personalNamespace in PersonalNamespaces(client))
        {
            // LIST "<prefix>*" returns the whole tree; LSUB or "%" would miss unsubscribed or nested folders.
            var listed = await client.GetFoldersAsync(personalNamespace, StatusItems.None, subscribedOnly: false, cancellationToken).ConfigureAwait(false);
            folders.AddRange(listed.Where(f => IsSelectable(f) && !IsInbox(f.FullName) && seen.Add(f.FullName)));
        }

        folders.Sort(CompareByPath);
        return folders;
    }

    /// <summary>
    /// The folder a backed-up folder is restored into: the folder of that name if the account has
    /// it, otherwise it is created together with any missing parent folders. The path is mapped
    /// onto the account's own layout, so a backup of a "/" server can go into a "." server whose
    /// folders all live below INBOX, and vice versa.
    /// </summary>
    /// <param name="fullName">Full folder name as recorded in the backup.</param>
    /// <param name="separator">Hierarchy separator used in <paramref name="fullName"/>; null to assume the account's own.</param>
    public async Task<IMailFolder> GetOrCreateFolderAsync(ImapClient client, string fullName, char? separator, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fullName);

        if (IsInbox(fullName))
        {
            return InboxOf(client);
        }

        var root = client.GetFolder(PersonalNamespaces(client)[0]);
        var sourceSeparator = separator ?? root.DirectorySeparator;

        if (sourceSeparator == root.DirectorySeparator)
        {
            try
            {
                return await client.GetFolderAsync(fullName, cancellationToken).ConfigureAwait(false);
            }
            catch (FolderNotFoundException)
            {
                // created below
            }
        }

        var segments = sourceSeparator == '\0'
            ? new[] { fullName }
            : fullName.Split(sourceSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException($"'{fullName}' does not name a folder.", nameof(fullName));
        }

        // A leading INBOX stays INBOX, whatever the separator; everything else goes below the
        // personal namespace (the top level, or INBOX on servers that keep all folders there).
        IMailFolder folder = root;
        var start = 0;
        if (segments.Length > 1 && IsInbox(segments[0]))
        {
            folder = InboxOf(client);
            start = 1;
        }

        for (var i = start; i < segments.Length; i++)
        {
            var subfolders = await folder.GetSubfoldersAsync(false, cancellationToken).ConfigureAwait(false);
            folder = subfolders.FirstOrDefault(f => string.Equals(f.Name, segments[i], StringComparison.Ordinal))
                     ?? subfolders.FirstOrDefault(f => string.Equals(f.Name, segments[i], StringComparison.OrdinalIgnoreCase))
                     ?? await folder.CreateAsync(segments[i], isMessageFolder: true, cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException($"The server did not create the folder '{segments[i]}'.");
        }

        return folder;
    }

    private static IReadOnlyList<FolderNamespace> PersonalNamespaces(ImapClient client) =>
        client.PersonalNamespaces.Count > 0
            ? client.PersonalNamespaces.ToList()
            : new[] { new FolderNamespace(client.Inbox?.DirectorySeparator ?? '/', string.Empty) };

    private static bool IsSelectable(IMailFolder folder) =>
        !string.IsNullOrEmpty(folder.FullName)
        && !folder.Attributes.HasFlag(FolderAttributes.NoSelect)
        && !folder.Attributes.HasFlag(FolderAttributes.NonExistent);

    private static IMailFolder InboxOf(ImapClient client) =>
        client.Inbox ?? throw new InvalidOperationException("The server did not report an INBOX.");

    private static bool IsInbox(string fullName) => string.Equals(fullName, "INBOX", StringComparison.OrdinalIgnoreCase);

    /// <summary>Segment by segment, so "Archiv/2023" sorts right after "Archiv" and before "Archiv-Alt".</summary>
    private static int CompareByPath(IMailFolder x, IMailFolder y)
    {
        var a = Segments(x);
        var b = Segments(y);
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aInbox = IsInbox(a[i]);
            var bInbox = IsInbox(b[i]);
            if (aInbox != bInbox)
            {
                return aInbox ? -1 : 1;
            }

            var order = StringComparer.InvariantCultureIgnoreCase.Compare(a[i], b[i]);
            if (order == 0)
            {
                order = string.CompareOrdinal(a[i], b[i]);
            }

            if (order != 0)
            {
                return order;
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    private static string[] Segments(IMailFolder folder) =>
        folder.DirectorySeparator == '\0' ? new[] { folder.FullName } : folder.FullName.Split(folder.DirectorySeparator);
}
