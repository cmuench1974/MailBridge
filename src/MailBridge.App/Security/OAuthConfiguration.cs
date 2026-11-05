using System.Diagnostics;
using System.Reflection;

namespace MailBridge.App.Security;

/// <summary>
/// OAuth2 settings compiled into this build. The Microsoft client ID is set once in
/// Directory.Build.props (MicrosoftOAuthClientId) and embedded as assembly metadata.
/// </summary>
internal static class OAuthConfiguration
{
    public static string MicrosoftClientId { get; } =
        typeof(OAuthConfiguration).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "MicrosoftOAuthClientId")?.Value?.Trim() ?? string.Empty;

    /// <summary>Opens the sign-in page in the user's default browser.</summary>
    public static Task OpenInBrowserAsync(Uri uri)
    {
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        return Task.CompletedTask;
    }
}
