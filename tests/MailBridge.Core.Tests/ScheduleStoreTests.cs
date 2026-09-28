using MailBridge.Core.Models;
using MailBridge.Core.Services;
using Xunit;

namespace MailBridge.Core.Tests;

public class ScheduleStoreTests
{
    [Fact]
    public void UpsertThenLoad_RoundTripsSchedule()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mailbridge-test-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ScheduleStore(path);
            var schedule = new BackupSchedule
            {
                AccountId = Guid.NewGuid(),
                AccountDisplayName = "Test Account",
                DestinationDirectory = @"D:\Backups",
                Frequency = ScheduleFrequency.Weekly,
                DayOfWeek = DayOfWeek.Monday,
                TimeOfDay = new TimeSpan(3, 30, 0),
            };

            store.Upsert(schedule);
            var loaded = store.Load();

            Assert.Single(loaded);
            Assert.Equal(schedule.AccountDisplayName, loaded[0].AccountDisplayName);
            Assert.Equal(ScheduleFrequency.Weekly, loaded[0].Frequency);
            Assert.Equal(DayOfWeek.Monday, loaded[0].DayOfWeek);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Remove_DeletesScheduleById()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mailbridge-test-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ScheduleStore(path);
            var schedule = new BackupSchedule
            {
                AccountId = Guid.NewGuid(),
                DestinationDirectory = @"D:\Backups",
            };
            store.Upsert(schedule);

            store.Remove(schedule.Id);

            Assert.Empty(store.Load());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
