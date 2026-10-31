using MailBridge.App.Localization;
using MailBridge.Core.Models;

namespace MailBridge.App.ViewModels;

/// <summary>
/// One entry of the provider template list. <see cref="Template"/> is null for
/// "Manual entry", whose name is resolved on read: the view model outlives the
/// window that gets rebuilt when the UI language changes.
/// </summary>
public sealed class MailTemplateOption
{
    public MailTemplateOption(MailProviderTemplate? template)
    {
        Template = template;
    }

    public MailProviderTemplate? Template { get; }

    public string Name => Template?.Name ?? Strings.Get("accounts.templateManual");

    public override string ToString() => Name;
}
