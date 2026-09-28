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

    private Visibility ToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;
}
