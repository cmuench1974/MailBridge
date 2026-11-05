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

    public async Task<IReadOnlyList<IMailFolder>> GetAllFoldersAsync(ImapClient client, CancellationToken cancellationToken = default)
    {
        var personal = client.GetFolder(client.PersonalNamespaces[0]);
        var folders = new List<IMailFolder>();

        if (client.Inbox is not null && IsSelectable(client.Inbox))
        {
            folders.Add(client.Inbox);
        }

        foreach (var folder in await personal.GetSubfoldersAsync(true, cancellationToken).ConfigureAwait(false))
        {
            if (!IsSelectable(folder))
            {
                continue;
            }

            if (client.Inbox is not null && string.Equals(folder.FullName, client.Inbox.FullName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            folders.Add(folder);
        }

        return folders;
    }

    private static bool IsSelectable(IMailFolder folder) =>
        !string.IsNullOrEmpty(folder.FullName) && !folder.Attributes.HasFlag(FolderAttributes.NoSelect);
}
