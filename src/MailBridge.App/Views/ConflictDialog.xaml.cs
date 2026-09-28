using MailBridge.Core.Models;
using MailBridge.Core.Services;
using Microsoft.UI.Xaml.Controls;

namespace MailBridge.App.Views;

/// <summary>
/// Shown once per conflicting (not confirmed-identical) message during a
/// restore run. Confirmed duplicates never reach this dialog - they are
/// skipped automatically because every compared attribute already matched.
/// </summary>
public sealed partial class ConflictDialog : ContentDialog
{
    public string SubjectText { get; }
    public string MatchedText { get; }
    public string DifferingText { get; }

    public ConflictDialog(RestoreConflict conflict)
    {
        InitializeComponent();

        SubjectText = $"Subject: {conflict.Candidate.Subject ?? "(no subject)"} - From: {conflict.Candidate.From}";
        MatchedText = "Matching attributes: " + (conflict.DuplicateCheck.MatchedAttributes.Count > 0
            ? string.Join(", ", conflict.DuplicateCheck.MatchedAttributes)
            : "(none)");
        DifferingText = "Differing attributes: " + string.Join(", ", conflict.DuplicateCheck.DifferingAttributes);
    }

    public async Task<ConflictResolution> ShowAndGetResolutionAsync()
    {
        var result = await ShowAsync();

        var applyToAll = ApplyToAllCheckBox.IsChecked == true;

        return result switch
        {
            ContentDialogResult.Primary => applyToAll ? ConflictResolution.ReplaceAll : ConflictResolution.Replace,
            ContentDialogResult.Secondary => applyToAll ? ConflictResolution.SkipAll : ConflictResolution.Skip,
            _ => ConflictResolution.Abort,
        };
    }
}
