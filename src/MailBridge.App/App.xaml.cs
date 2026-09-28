using Microsoft.UI.Xaml;

namespace MailBridge.App;

public partial class App : Application
{
    private Window? _window;

    public static Window MainWindowInstance { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWindowInstance = _window;
        _window.Activate();
    }
}
