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

    private void SelectContentsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.BackupPath))
        {
            _ = ShowInfoDialogAsync(Localization.Strings.Get("restore.selectFirst"));
            return;
        }

        var button = (Button)sender;
        button.IsEnabled = false;

        var window = new SelectionWindow(
            Localization.Strings.Get("restore.selTitle"),
            ViewModel.BackupFolders,
            null,
            () => ViewModel.LoadContentsCommand.ExecuteAsync(null),
            () => ViewModel.SelectionSummary,
            () => ViewModel.StatusText);
        window.Closed += (_, _) => button.IsEnabled = true;
        window.Activate();
    }

    private async Task ShowInfoDialogAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Content = message,
            CloseButtonText = Localization.Strings.Get("common.ok"),
        };
        await dialog.ShowAsync();
    }

    private Visibility ToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;
}
