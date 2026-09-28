using System.Collections.ObjectModel;
using MailBridge.App.ViewModels;
using MailBridge.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MailBridge.App.Views;

public sealed partial class BackupPage : Page
{
    public string L(string key) => Localization.Strings.Get(key);
    public BackupViewModel ViewModel => AppState.Backup;

    public ObservableCollection<EmailAccount> Accounts => AppState.Accounts.Accounts;

    public BackupPage()
    {
        InitializeComponent();
    }

    private async void BrowseButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance));
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.DestinationDirectory = folder.Path;
        }
    }

    private async void SelectFoldersButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        var opened = false;
        try
        {
            if (ViewModel.ServerFolders.Count == 0)
            {
                await ViewModel.LoadFoldersCommand.ExecuteAsync(null);
                if (ViewModel.ServerFolders.Count == 0)
                {
                    return;
                }
            }

            var window = new SelectionWindow(
                Localization.Strings.Get("backup.selTitle"),
                ViewModel.ServerFolders,
                ViewModel.LoadFolderMessagesAsync,
                () => ViewModel.LoadFoldersCommand.ExecuteAsync(null),
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
