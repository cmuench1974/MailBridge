using System.ComponentModel;
using System.Runtime.InteropServices;
using MailBridge.App.Localization;
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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    public string L(string key) => Strings.Get(key);

    private string _appliedLanguage;

    public MainWindow()
    {
        InitializeComponent();
        Title = Strings.Get("app.windowTitle"); // taskbar / Alt+Tab

        // Extend content into the title bar and use the custom TitleBar as drag region.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var version = typeof(App).Assembly.GetName().Version;
        AppTitleBar.Subtitle = version is null ? string.Empty : Strings.Get("app.version", version.ToString(3));

        ConfigureWindow();

        _appliedLanguage = AppState.Settings.SelectedLanguageCode;
        AppState.Theme.Register(this);
        AppState.Theme.ApplyTheme(AppState.Settings.SelectedTheme);
        AppState.Settings.PropertyChanged += OnSettingsPropertyChanged;
        AppState.Settings.Saved += OnSettingsSaved;
        Closed += (_, _) =>
        {
            AppState.Settings.PropertyChanged -= OnSettingsPropertyChanged;
            AppState.Settings.Saved -= OnSettingsSaved;
        };
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.SelectedTheme))
        {
            AppState.Theme.ApplyTheme(AppState.Settings.SelectedTheme);
        }
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        if (AppState.Settings.SelectedLanguageCode != _appliedLanguage)
        {
            // Language applies to every page label and all localized
            // strings - the cleanest way to rebind everything is a fresh
            // shell. Settings are already persisted at this point.
            AppState.Settings.Saved -= OnSettingsSaved;
            _appliedLanguage = AppState.Settings.SelectedLanguageCode;

            var replacement = new MainWindow();
            App.MainWindowInstance = replacement;
            replacement.Activate();
            Close();
            return;
        }

        AppState.Theme.ApplyTheme(AppState.Settings.SelectedTheme);
    }

    private void ConfigureWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }

        // AppWindow works in physical pixels: scale the default size by the display scaling and
        // clamp it to the work area of the monitor the window appears on.
        var area = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;
        var scale = Math.Max(1, GetDpiForWindow(hwnd)) / 96.0;
        var width = Math.Min((int)(DefaultWidth * scale), area.Width);
        var height = Math.Min((int)(DefaultHeight * scale), area.Height);
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2,
            width,
            height));
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        RootNavigationView.IsPaneOpen = !RootNavigationView.IsPaneOpen;
    }

    private void RootNavigationView_Loaded(object sender, RoutedEventArgs e)
    {
        // Start on the backup page - unless no account is set up yet, which has to happen first.
        var startTag = AppState.Accounts.Accounts.Count == 0 ? "accounts" : "backup";
        RootNavigationView.SelectedItem = RootNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .First(item => (string)item.Tag == startTag);
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
