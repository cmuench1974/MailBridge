using MailBridge.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MailBridge.App.Views;

public sealed partial class SchedulePage : Page
{
    public SchedulesViewModel ViewModel => AppState.Schedules;

    public SchedulePage()
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
}
