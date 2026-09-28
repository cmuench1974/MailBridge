using MailBridge.Core.Models;

namespace MailBridge.Core.Services;

/// <summary>
/// Persists the list of configured backup schedules. The Windows Task
/// Scheduler entry for each schedule is managed separately (App project);
/// this store is just the source of truth for what each task should do
/// when it fires, and for run history shown in the UI.
/// </summary>
public sealed class ScheduleStore
{
    private readonly string _path;

    public ScheduleStore(string path)
    {
        _path = path;
    }

    public List<BackupSchedule> Load() => JsonFileStore.Load<BackupSchedule>(_path);

    public void Save(IEnumerable<BackupSchedule> schedules) => JsonFileStore.Save(_path, schedules.ToList());

    public void Upsert(BackupSchedule schedule)
    {
        var all = Load();
        var index = all.FindIndex(s => s.Id == schedule.Id);
        if (index >= 0)
        {
            all[index] = schedule;
        }
        else
        {
            all.Add(schedule);
        }

        Save(all);
    }

    public void Remove(Guid scheduleId)
    {
        var all = Load();
        all.RemoveAll(s => s.Id == scheduleId);
        Save(all);
    }
}
