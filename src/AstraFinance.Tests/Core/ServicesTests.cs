using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Localization;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Services;

namespace AstraFinance.Tests.Core;

public class ServicesTests
{
    [Theory]
    [InlineData(4, 0, "Greeting.Morning")]
    [InlineData(10, 59, "Greeting.Morning")]
    [InlineData(11, 0, "Greeting.Midday")]
    [InlineData(15, 0, "Greeting.Afternoon")]
    [InlineData(18, 0, "Greeting.Night")]
    [InlineData(2, 0, "Greeting.Night")]
    public void Greeting_follows_time_of_day(int hour, int minute, string expected) =>
        Assert.Equal(expected, GreetingProvider.GetGreetingKey(new TimeOnly(hour, minute)));

    [Fact]
    public void Page_catalog_has_nine_pages_in_spec_order_with_dashboard_first()
    {
        Assert.Equal(
            [AppPage.Dashboard, AppPage.Transactions, AppPage.Accounts, AppPage.Budgets, AppPage.SavingsGoals,
             AppPage.Bills, AppPage.Analytics, AppPage.Reports, AppPage.Settings],
            PageCatalog.All.Select(p => p.Page));
        Assert.Equal(AppPage.Dashboard, PageCatalog.StartupPage);
    }

    [Fact]
    public void Notification_center_tracks_unread_and_bounds_history()
    {
        var center = new NotificationCenter(new FakeClock());
        for (var i = 0; i < NotificationCenter.MaxHistory + 5; i++)
        {
            center.Notify(NotificationKind.Info, $"T{i}", "m");
        }

        Assert.Equal(NotificationCenter.MaxHistory, center.History.Count);
        Assert.Equal(NotificationCenter.MaxHistory, center.UnreadCount);
        Assert.Equal($"T{NotificationCenter.MaxHistory + 4}", center.History[0].Title);

        center.MarkAllRead();
        Assert.Equal(0, center.UnreadCount);

        center.Remove(center.History[0].Id);
        Assert.Equal(NotificationCenter.MaxHistory - 1, center.History.Count);

        center.Clear();
        Assert.Empty(center.History);
    }

    [Fact]
    public async Task Preferences_update_applies_raises_event_and_persists()
    {
        var store = new InMemoryPreferencesStore();
        var service = new PreferencesService(store, new NotificationCenter(new FakeClock()), new LocalizationService(AppLanguage.English));
        await service.InitializeAsync();
        PreferencesChangedEventArgs? args = null;
        service.Changed += (_, e) => args = e;

        var ok = await service.UpdateAsync(p => p with { ThemeMode = AppThemeMode.Dark });

        Assert.True(ok);
        Assert.Equal(AppThemeMode.Dark, service.Current.ThemeMode);
        Assert.Equal(AppThemeMode.Dark, store.Saved!.ThemeMode);
        Assert.Equal(AppThemeMode.System, args!.Previous.ThemeMode);
    }

    [Fact]
    public async Task Unchanged_update_does_not_write()
    {
        var store = new InMemoryPreferencesStore();
        var service = new PreferencesService(store, new NotificationCenter(new FakeClock()), new LocalizationService(AppLanguage.English));
        await service.UpdateAsync(p => p with { });
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Failed_save_keeps_session_change_and_notifies_user()
    {
        var store = new InMemoryPreferencesStore { FailSaves = true };
        var notifications = new NotificationCenter(new FakeClock());
        var service = new PreferencesService(store, notifications, new LocalizationService(AppLanguage.English));

        var ok = await service.UpdateAsync(p => p with { Accent = AccentColor.Teal });

        Assert.False(ok);
        Assert.Equal(AccentColor.Teal, service.Current.Accent);
        var notice = Assert.Single(notifications.History);
        Assert.Equal(NotificationKind.Error, notice.Kind);
        Assert.Equal("Preferensi tidak tersimpan", notice.Title); // default preference language is Indonesian
    }
}
