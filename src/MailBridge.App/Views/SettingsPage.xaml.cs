using System.Diagnostics;
using MailBridge.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MailBridge.App.Views;

public sealed partial class SettingsPage : Page
{
    public string L(string key) => Localization.Strings.Get(key);
    public SettingsViewModel ViewModel => AppState.Settings;

    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance));
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.DefaultBackupDirectory = folder.Path;
        }
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppState.DataDirectory}\""));
    }
}
