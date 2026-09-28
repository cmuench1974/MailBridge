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
        // When launched by the Windows Task Scheduler for a scheduled
        // backup (see MailBridge.App.Scheduling.WindowsTaskSchedulerService),
        // run the backup headlessly and exit without showing any window.
        var scheduleId = TryGetScheduledBackupId();
        if (scheduleId.HasValue)
        {
            _ = RunScheduledBackupAndExitAsync(scheduleId.Value);
            return;
        }

        _window = new MainWindow();
        MainWindowInstance = _window;
        _window.Activate();
    }

    private static Guid? TryGetScheduledBackupId()
    {
        var commandLineArgs = Environment.GetCommandLineArgs();
        for (var i = 0; i < commandLineArgs.Length - 1; i++)
        {
            if (commandLineArgs[i] == "--run-scheduled-backup" && Guid.TryParse(commandLineArgs[i + 1], out var id))
            {
                return id;
            }
        }

        return null;
    }

    private static async Task RunScheduledBackupAndExitAsync(Guid scheduleId)
    {
        try
        {
            await AppState.ScheduledBackupRunner.RunAsync(scheduleId);
        }
        finally
        {
            Environment.Exit(0);
        }
    }
}
