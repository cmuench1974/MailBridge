using MailBridge.Core.Security.OAuth;
using System.Net;
using System.Text;
using MailBridge.Core.Security;


namespace MailBridge.Core.Tests.OAuth;

/// <summary>Answers token requests from a callback and records the posted forms.</summary>
internal sealed class StubTokenEndpoint : HttpMessageHandler
{
    private readonly Func<Dictionary<string, string>, (HttpStatusCode Status, string Body)> _respond;

    public StubTokenEndpoint(Func<Dictionary<string, string>, (HttpStatusCode Status, string Body)> respond)
    {
        _respond = respond;
    }

    public List<Dictionary<string, string>> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var form = System.Web.HttpUtility.ParseQueryString(body);
        var fields = form.AllKeys.Where(k => k is not null).ToDictionary(k => k!, k => form[k]!);
        lock (Requests)
        {
            Requests.Add(fields);
        }

        var (status, responseBody) = _respond(fields);
        return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
    }

    public static string TokenJson(string accessToken, string? refreshToken = null, int expiresIn = 3600, string? idToken = null)
    {
        var parts = new List<string> { $"\"access_token\":\"{accessToken}\"", $"\"expires_in\":{expiresIn}", "\"token_type\":\"Bearer\"" };
        if (refreshToken is not null)
        {
            parts.Add($"\"refresh_token\":\"{refreshToken}\"");
        }

        if (idToken is not null)
        {
            parts.Add($"\"id_token\":\"{idToken}\"");
        }

        return "{" + string.Join(',', parts) + "}";
    }

    /// <summary>Unsigned JWT with the given payload JSON (signature is never checked).</summary>
    public static string IdToken(string payloadJson) =>
        $"{Base64Url("{\"alg\":\"none\"}")}.{Base64Url(payloadJson)}.sig";

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class InMemoryCredentialStore : ICredentialStore
{
    public Dictionary<(string Key, string User), string> Entries { get; } = new();

    public void Save(string credentialKey, string username, string secret) => Entries[(credentialKey, username)] = secret;

    public string? TryGet(string credentialKey, string username) =>
        Entries.TryGetValue((credentialKey, username), out var secret) ? secret : null;

    public void Remove(string credentialKey, string username) => Entries.Remove((credentialKey, username));
}

internal sealed class ManualTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class TestOptions
{
    public static OAuthProviderOptions Provider(string clientId = "test-client") => new(
        clientId,
        new Uri("https://login.example.test/authorize"),
        new Uri("https://login.example.test/token"),
        new[] { "imap", "offline_access" },
        new Dictionary<string, string> { ["prompt"] = "select_account" });

    public static readonly BrowserPageText Page = new("MailBridge", "Signed in.", "Sign-in failed.");
}
