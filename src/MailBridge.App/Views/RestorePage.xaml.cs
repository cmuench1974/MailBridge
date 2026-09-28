using System.Collections.ObjectModel;
using MailBridge.App.ViewModels;
using MailBridge.Core.Models;
using MailBridge.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MailBridge.App.Views;

public sealed partial class RestorePage : Page
{
    public string L(string key) => Localization.Strings.Get(key);
    public RestoreViewModel ViewModel => AppState.Restore;

    public ObservableCollection<EmailAccount> Accounts => AppState.Accounts.Accounts;

    public RestorePage()
    {
        InitializeComponent();
        ViewModel.ConflictHandler = ShowConflictDialogAsync;
    }

    private async Task<ConflictResolution> ShowConflictDialogAsync(RestoreConflict conflict)
    {
        var dialog = new ConflictDialog(conflict) { XamlRoot = this.XamlRoot };
        return await dialog.ShowAndGetResolutionAsync();
    }

    private async void BrowseButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance));
        picker.FileTypeFilter.Add(".zip");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.BackupPath = file.Path;
        }
    }

    private async void SelectContentsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        var opened = false;
        try
        {
            if (ViewModel.BackupFolders.Count == 0)
            {
                await ViewModel.LoadContentsCommand.ExecuteAsync(null);
                if (ViewModel.BackupFolders.Count == 0)
                {
                    return;
                }
            }

            var window = new SelectionWindow(
                Localization.Strings.Get("restore.selTitle"),
                ViewModel.BackupFolders,
                null,
                () => ViewModel.LoadContentsCommand.ExecuteAsync(null),
                () => ViewModel.SelectionSummary);
            window.Closed += (_, _) => button.IsEnabled = true;
            window.Activate();
            opened = true;
        }
        finally
        {
            if (!opened)
            {
                button.IsEnabled = true;
            }
        }
    }

    private Visibility ToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;
}
