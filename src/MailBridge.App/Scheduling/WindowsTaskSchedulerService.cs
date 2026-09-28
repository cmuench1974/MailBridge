using System.Diagnostics;
using MailBridge.Core.Models;

namespace MailBridge.App.Scheduling;

/// <summary>
/// Registers/removes Windows Task Scheduler entries that launch MailBridge
/// with a headless "--run-scheduled-backup &lt;id&gt;" argument at the
/// configured time. This is what actually triggers a backup when the app
/// isn't open - the in-app schedule list is just the description of what
/// each task should do (see <see cref="MailBridge.Core.Services.ScheduleStore"/>).
///
/// Implemented via schtasks.exe rather than a Task Scheduler COM/NuGet
/// dependency to keep this simple and avoid extra packaging concerns for a
/// full-trust MSIX app.
/// </summary>
public sealed class WindowsTaskSchedulerService
{
    private const string TaskNamePrefix = "MailBridge_Backup_";

    public void RegisterOrUpdate(BackupSchedule schedule)
    {
        Remove(schedule.Id);

        if (!schedule.Enabled)
        {
            return;
        }

        var exePath = Process.GetCurrentProcess().MainModule?.FileName
                      ?? throw new InvalidOperationException("Could not determine the running executable path.");

        var taskName = TaskNamePrefix + schedule.Id;
        var time = schedule.TimeOfDay.ToString(@"hh\:mm");
        var scheduleArgs = schedule.Frequency == ScheduleFrequency.Weekly
            ? $"/SC WEEKLY /D {ToScheduleDay(schedule.DayOfWeek)}"
            : "/SC DAILY";

        var taskRun = $"\"{exePath}\" --run-scheduled-backup {schedule.Id}";

        var createArgs = $"/Create /TN \"{taskName}\" /TR \"{taskRun}\" {scheduleArgs} /ST {time} /F";
        RunSchTasks(createArgs);
    }

    public void Remove(Guid scheduleId)
    {
        var taskName = TaskNamePrefix + scheduleId;
        // Ignore failures - the task may simply not exist yet.
        RunSchTasks($"/Delete /TN \"{taskName}\" /F", ignoreFailure: true);
    }

    private static string ToScheduleDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MON",
        DayOfWeek.Tuesday => "TUE",
        DayOfWeek.Wednesday => "WED",
        DayOfWeek.Thursday => "THU",
        DayOfWeek.Friday => "FRI",
        DayOfWeek.Saturday => "SAT",
        DayOfWeek.Sunday => "SUN",
        _ => "SUN",
    };

    private static void RunSchTasks(string arguments, bool ignoreFailure = false)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
                             ?? throw new InvalidOperationException("Failed to start schtasks.exe.");
        process.WaitForExit();

        if (!ignoreFailure && process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"schtasks.exe failed ({process.ExitCode}): {error}");
        }
    }
}
