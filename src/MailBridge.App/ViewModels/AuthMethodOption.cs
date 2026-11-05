using MailBridge.App.Localization;
using MailBridge.Core.Models;

namespace MailBridge.App.ViewModels;

/// <summary>
/// One entry of the sign-in method list. There is exactly one instance per method, so the
/// ComboBox keeps its selection when the list is rebuilt for another provider template; the
/// name is resolved on read because the UI language can change at runtime.
/// </summary>
public sealed class AuthMethodOption
{
    private static readonly Dictionary<AccountAuthMethod, AuthMethodOption> Instances =
        Enum.GetValues<AccountAuthMethod>().ToDictionary(m => m, m => new AuthMethodOption(m));

    private AuthMethodOption(AccountAuthMethod method)
    {
        Method = method;
    }

    public AccountAuthMethod Method { get; }

    public string Name => Strings.Get($"accounts.method.{Method}");

    public static AuthMethodOption For(AccountAuthMethod method) => Instances[method];

    public override string ToString() => Name;
}
