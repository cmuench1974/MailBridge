using MailBridge.Core.Models;
using MailBridge.Core.Services;
using Xunit;

namespace MailBridge.Core.Tests;

public class MailProviderTemplatesTests
{
    [Fact]
    public void All_HasUniqueNamesAndHosts()
    {
        var templates = MailProviderTemplates.All;

        Assert.Equal(templates.Count, templates.Select(t => t.Name).Distinct().Count());
        Assert.Equal(templates.Count, templates.Select(t => t.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("imap.mail.eu-west-1.awsapps.com")]
    [InlineData("imap.mail.us-east-1.awsapps.com")]
    [InlineData("imap.mail.us-west-2.awsapps.com")]
    [InlineData("mail.eclipso.de")]
    public void All_ContainsRequiredProviders(string host)
    {
        Assert.NotNull(MailProviderTemplates.Find(host, 993));
    }

    [Fact]
    public void Find_IgnoresCaseAndSurroundingWhitespace()
    {
        var template = MailProviderTemplates.Find("  IMAP.GMX.NET ", 993);

        Assert.Equal("GMX", template?.Name);
        Assert.Equal(MailProviderSetup.EnableImap, template?.Setup);
    }

    [Theory]
    [InlineData("imap.gmx.net", 143)]
    [InlineData("imap.example.com", 993)]
    [InlineData("", 993)]
    [InlineData(null, 993)]
    public void Find_ReturnsNull_WhenHostOrPortDoesNotMatch(string? host, int port)
    {
        Assert.Null(MailProviderTemplates.Find(host, port));
    }
}
