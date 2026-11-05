using System.Diagnostics;
using System.Reflection;
using MailBridge.Core.Models;
using MailBridge.Core.Security.OAuth;

namespace MailBridge.App.Security;

/// <summary>
/// OAuth2 settings compiled into this build. The client IDs (and Google's client secret) are
/// MSBuild properties from Directory.Build.props / OAuth.local.props, embedded as assembly metadata.
/// </summary>
internal static class OAuthConfiguration
{
    /// <summary>One entry per OAuth2 sign-in method; unconfigured providers are included but report IsConfigured = false.</summary>
    public static IReadOnlyDictionary<AccountAuthMethod, OAuthProviderOptions> Providers { get; } =
        new Dictionary<AccountAuthMethod, OAuthProviderOptions>
        {
            [AccountAuthMethod.MicrosoftOAuth2] = MicrosoftOAuth.CreateOptions(Read("MicrosoftOAuthClientId")),
            [AccountAuthMethod.GoogleOAuth2] = GoogleOAuth.CreateOptions(Read("GoogleOAuthClientId"), Read("GoogleOAuthClientSecret")),
        };

    /// <summary>Opens the sign-in page in the user's default browser.</summary>
    public static Task OpenInBrowserAsync(Uri uri)
    {
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        return Task.CompletedTask;
    }

    private static string Read(string key) =>
        typeof(OAuthConfiguration).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value?.Trim() ?? string.Empty;
}
