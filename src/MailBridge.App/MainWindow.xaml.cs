using MailBridge.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MailBridge.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "MailBridge";
    }

    private void RootNavigationView_Loaded(object sender, RoutedEventArgs e)
    {
        RootNavigationView.SelectedItem = RootNavigationView.MenuItems[0];
    }

    private void RootNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "accounts":
                    ContentFrame.Navigate(typeof(AccountsPage));
                    break;
                case "backup":
                    ContentFrame.Navigate(typeof(BackupPage));
                    break;
                case "restore":
                    ContentFrame.Navigate(typeof(RestorePage));
                    break;
                case "schedule":
                    ContentFrame.Navigate(typeof(SchedulePage));
                    break;
            }
        }
    }
}
