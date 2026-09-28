using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailBridge.App.Scheduling;
using MailBridge.Core.Models;
using MailBridge.Core.Services;

namespace MailBridge.App.ViewModels;

public partial class SchedulesViewModel : ObservableObject
{
    private readonly ScheduleStore _scheduleStore;
    private readonly WindowsTaskSchedulerService _taskSchedulerService;

    public ObservableCollection<EmailAccount> Accounts { get; }

    public ObservableCollection<BackupSchedule> Schedules { get; } = new();

    public IReadOnlyList<ScheduleFrequency> Frequencies { get; } = Enum.GetValues<ScheduleFrequency>();

    public IReadOnlyList<DayOfWeek> DaysOfWeek { get; } = Enum.GetValues<DayOfWeek>();

    [ObservableProperty]
    private EmailAccount? selectedAccount;

    [ObservableProperty]
    private string destinationDirectory = string.Empty;

    [ObservableProperty]
    private bool compressToZip = true;

    [ObservableProperty]
    private ScheduleFrequency frequency = ScheduleFrequency.Daily;

    [ObservableProperty]
    private DayOfWeek dayOfWeek = DayOfWeek.Sunday;

    [ObservableProperty]
    private TimeSpan timeOfDay = new(2, 0, 0);

    [ObservableProperty]
    private string statusText = string.Empty;

    public SchedulesViewModel(ScheduleStore scheduleStore, WindowsTaskSchedulerService taskSchedulerService, ObservableCollection<EmailAccount> accounts, SettingsStore settingsStore)
    {
        _scheduleStore = scheduleStore;
        _taskSchedulerService = taskSchedulerService;
        Accounts = accounts;

        var settings = settingsStore.Load();
        if (!string.IsNullOrWhiteSpace(settings.DefaultBackupDirectory))
        {
            DestinationDirectory = settings.DefaultBackupDirectory;
        }
        CompressToZip = settings.DefaultCompressToZip;

        foreach (var schedule in _scheduleStore.Load())
        {
            Schedules.Add(schedule);
        }
    }

    [RelayCommand]
    private void AddSchedule()
    {
        if (SelectedAccount is null || string.IsNullOrWhiteSpace(DestinationDirectory))
        {
            StatusText = Localization.Strings.Get("common.selectFirst");
            return;
        }

        var schedule = new BackupSchedule
        {
            AccountId = SelectedAccount.Id,
            AccountDisplayName = SelectedAccount.DisplayName,
            DestinationDirectory = DestinationDirectory,
            CompressToZip = CompressToZip,
            Frequency = Frequency,
            DayOfWeek = DayOfWeek,
            TimeOfDay = TimeOfDay,
            Enabled = true,
        };

        try
        {
            _taskSchedulerService.RegisterOrUpdate(schedule);
            _scheduleStore.Upsert(schedule);
            Schedules.Add(schedule);
            StatusText = Localization.Strings.Get("schedule.created", schedule.AccountDisplayName);
            DestinationDirectory = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = Localization.Strings.Get("schedule.createFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void ToggleEnabled(BackupSchedule schedule)
    {
        schedule.Enabled = !schedule.Enabled;
        try
        {
            _taskSchedulerService.RegisterOrUpdate(schedule);
            _scheduleStore.Upsert(schedule);
            StatusText = schedule.Enabled
                ? Localization.Strings.Get("schedule.enabledMsg")
                : Localization.Strings.Get("schedule.disabledMsg");
        }
        catch (Exception ex)
        {
            StatusText = Localization.Strings.Get("schedule.updateFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void RemoveSchedule(BackupSchedule schedule)
    {
        _taskSchedulerService.Remove(schedule.Id);
        _scheduleStore.Remove(schedule.Id);
        Schedules.Remove(schedule);
        StatusText = Localization.Strings.Get("schedule.removed");
    }
}
