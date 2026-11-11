using AstraFinance.App.ViewModels.Pages;
using AstraFinance.Core.Interfaces;

namespace AstraFinance.Tests.App;

public class BackupHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_backup_is_reported_as_none() =>
        Assert.Equal(BackupHealth.None, DataSettingsViewModel.EvaluateHealth(null, BackupSchedule.Weekly, Now));

    [Theory]
    [InlineData(BackupSchedule.Daily, 1, BackupHealth.Current)]
    [InlineData(BackupSchedule.Daily, 3, BackupHealth.Stale)]
    [InlineData(BackupSchedule.Weekly, 7, BackupHealth.Current)]
    [InlineData(BackupSchedule.Weekly, 9, BackupHealth.Stale)]
    [InlineData(BackupSchedule.Monthly, 31, BackupHealth.Current)]
    [InlineData(BackupSchedule.Monthly, 40, BackupHealth.Stale)]
    [InlineData(BackupSchedule.Off, 29, BackupHealth.Current)]
    [InlineData(BackupSchedule.Off, 31, BackupHealth.Stale)]
    public void Recent_enough_depends_on_the_schedule(BackupSchedule schedule, int daysAgo, BackupHealth expected) =>
        Assert.Equal(expected, DataSettingsViewModel.EvaluateHealth(Now.AddDays(-daysAgo), schedule, Now));
}
