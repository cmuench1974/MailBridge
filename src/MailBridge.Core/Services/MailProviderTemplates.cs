using MailBridge.Core.Models;

namespace MailBridge.Core.Services;

/// <summary>
/// IMAP presets for popular mail providers. All of them use implicit TLS, the
/// only mode MailBridge connects with for accounts created in the UI. Providers
/// that only accept OAuth2 (Outlook.com, Microsoft 365) are left out on purpose:
/// a template would suggest a login that cannot succeed.
/// </summary>
public static class MailProviderTemplates
{
    public static IReadOnlyList<MailProviderTemplate> All { get; } = new MailProviderTemplate[]
    {
        new("AWS WorkMail (eu-west-1, Ireland)", "imap.mail.eu-west-1.awsapps.com", MailProviderSetup.None),
        new("AWS WorkMail (us-east-1, N. Virginia)", "imap.mail.us-east-1.awsapps.com", MailProviderSetup.None),
        new("AWS WorkMail (us-west-2, Oregon)", "imap.mail.us-west-2.awsapps.com", MailProviderSetup.None),
        new("eclipso", "mail.eclipso.de", MailProviderSetup.EnableImap),
        new("Fastmail", "imap.fastmail.com", MailProviderSetup.AppPassword),
        new("freenet", "mx.freenet.de", MailProviderSetup.None),
        new("Gmail", "imap.gmail.com", MailProviderSetup.AppPassword),
        new("GMX", "imap.gmx.net", MailProviderSetup.EnableImap),
        new("iCloud Mail", "imap.mail.me.com", MailProviderSetup.AppPassword),
        new("IONOS", "imap.ionos.de", MailProviderSetup.None),
        new("mailbox.org", "imap.mailbox.org", MailProviderSetup.None),
        new("Posteo", "posteo.de", MailProviderSetup.None),
        new("Strato", "imap.strato.de", MailProviderSetup.None),
        new("T-Online", "secureimap.t-online.de", MailProviderSetup.AppPassword),
        new("WEB.DE", "imap.web.de", MailProviderSetup.EnableImap),
        new("Yahoo Mail", "imap.mail.yahoo.com", MailProviderSetup.AppPassword),
        new("Zoho Mail (EU)", "imap.zoho.eu", MailProviderSetup.EnableImap),
        new("Zoho Mail (global)", "imap.zoho.com", MailProviderSetup.EnableImap),
    };

    /// <summary>Finds the template matching the given connection details, if any.</summary>
    public static MailProviderTemplate? Find(string? host, int port)
    {
        var trimmed = host?.Trim();
        return All.FirstOrDefault(t =>
            t.Port == port && string.Equals(t.Host, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
