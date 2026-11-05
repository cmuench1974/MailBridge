using MailBridge.App.Localization;
using MailBridge.Core.Services;

namespace MailBridge.App.ViewModels;

/// <summary>Localized texts for accounts that cannot sign in until the user acts.</summary>
internal static class CredentialErrors
{
    public static string Describe(CredentialUnavailableException ex) => ex.Reason switch
    {
        CredentialUnavailableReason.MissingPassword => Strings.Get("common.noPassword"),
        CredentialUnavailableReason.SignInRequired => Strings.Get("common.signInRequired"),
        CredentialUnavailableReason.OAuthNotConfigured => Strings.Get("accounts.oauthNotConfigured"),
        _ => ex.Message,
    };
}
