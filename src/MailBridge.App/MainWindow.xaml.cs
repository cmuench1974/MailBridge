using MailBridge.App.ViewModels;
using MailBridge.App.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MailBridge.App;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1180;
    private const int DefaultHeight = 780;

    public MainWindow()
    {
        InitializeComponent();
        Title = "MailBridge";

        ConfigureWindow();

        ApplyTheme();
        AppState.Settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.SelectedTheme))
            {
                ApplyTheme();
            }
        };
    }

    private void ConfigureWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (System.IO.File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }

        appWindow.Resize(new Windows.Graphics.SizeInt32(DefaultWidth, DefaultHeight));

        var area = DisplayArea.Primary.WorkArea;
        appWindow.Move(new Windows.Graphics.PointInt32(
            area.X + Math.Max(0, (area.Width - DefaultWidth) / 2),
            area.Y + Math.Max(0, (area.Height - DefaultHeight) / 2)));
    }

    private void ApplyTheme()
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = AppState.Settings.SelectedTheme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
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
                case "settings":
                    ContentFrame.Navigate(typeof(SettingsPage));
                    break;
            }
        }
    }
}
