using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace MailBridge.App.ViewModels;

/// <summary>
/// One folder row in the backup/restore selection trees. IsChecked is
/// tri-state: true = whole folder, false = excluded, null = partially
/// selected (some messages deselected).
/// </summary>
public partial class SelectableFolder : ObservableObject
{
    public required string Name { get; init; }

    public int TotalMessages { get; init; }

    /// <summary>Item key space: UIDs (backup) or relative .eml paths (restore).</summary>
    public ObservableCollection<SelectableMessage> Messages { get; } = new();

    public bool MessagesLoaded { get; set; }

    [ObservableProperty]
    private bool? isChecked = true;

    /// <summary>Set by the owning view model; invoked when a child checkbox changes.</summary>
    public Action? SelectionChanged { get; set; }

    partial void OnIsCheckedChanged(bool? value)
    {
        if (value.HasValue)
        {
            foreach (var message in Messages)
            {
                message.IsChecked = value.Value;
            }
        }

        SelectionChanged?.Invoke();
    }

    public void RefreshCheckedState()
    {
        if (!MessagesLoaded || Messages.Count == 0)
        {
            return;
        }

        var checkedCount = Messages.Count(m => m.IsChecked);
        IsChecked = checkedCount == 0 ? false : checkedCount == Messages.Count ? true : null;
    }

    public int SelectedMessageCount
    {
        get
        {
            if (!MessagesLoaded || IsChecked == false)
            {
                return 0;
            }

            if (IsChecked == true || !MessagesLoaded)
            {
                return TotalMessages;
            }

            return Messages.Count(m => m.IsChecked);
        }
    }
}

public partial class SelectableMessage : ObservableObject
{
    public required string ItemKey { get; init; }

    public string Subject { get; init; } = string.Empty;

    public string From { get; init; } = string.Empty;

    public string DateText { get; init; } = string.Empty;

    [ObservableProperty]
    private bool isChecked = true;

    partial void OnIsCheckedChanged(bool value) => SelectionChanged?.Invoke();

    /// <summary>Set by the owning view model; invoked when this checkbox changes.</summary>
    public Action? SelectionChanged { get; set; }
}
