using MailBridge.App.Localization;
using MailBridge.Core.Models;
using MailBridge.Core.Services;

namespace MailBridge.App.ViewModels;

/// <summary>Localized texts for accounts that cannot sign in until the user acts.</summary>
internal static class CredentialErrors
{
    public static string Describe(CredentialUnavailableException ex) => ex.Reason switch
    {
        CredentialUnavailableReason.MissingPassword => Strings.Get("common.noPassword"),
        CredentialUnavailableReason.SignInRequired => Strings.Get("common.signInRequired", ProviderName(ex.AuthMethod)),
        CredentialUnavailableReason.OAuthNotConfigured => NotConfigured(ex.AuthMethod),
        _ => ex.Message,
    };

    /// <summary>Brand name of the sign-in provider (not translated).</summary>
    public static string ProviderName(AccountAuthMethod method) => method switch
    {
        AccountAuthMethod.MicrosoftOAuth2 => "Microsoft",
        AccountAuthMethod.GoogleOAuth2 => "Google",
        _ => string.Empty,
    };

    public static string NotConfigured(AccountAuthMethod method) =>
        Strings.Get("accounts.oauthNotConfigured", ProviderName(method));
}
