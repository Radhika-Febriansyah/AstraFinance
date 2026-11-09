using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Services;

namespace AstraFinance.App.ViewModels.Pages;

/// <summary>
/// Dashboard. Phase 1 provides the greeting and current date. Financial cards, the cash-flow
/// chart, recent transactions and the budget summary are implemented in roadmap phase 5 and
/// will be derived from database records only.
/// </summary>
public sealed class DashboardViewModel : PageViewModel
{
    private readonly IClock _clock;
    private readonly CancellationTokenSource _cts = new();

    public DashboardViewModel(ILocalizationService localization, IClock clock)
        : base(AppPage.Dashboard, localization)
    {
        _clock = clock;
    }

    public string Greeting => Localization.GetString(GreetingProvider.GetGreetingKey(TimeOnly.FromDateTime(_clock.LocalNow)));

    public string DateText => _clock.LocalNow.ToString("D", Localization.Culture);

    public string ModuleNotice => Localization.Format(
        "Module.InDevelopment.Body", Localization.GetString("Nav.Dashboard"), Descriptor.PlannedPhase);

    public string PhaseBadge => Localization.Format("Module.InDevelopment.Badge", Descriptor.PlannedPhase);

    public override Task OnNavigatedToAsync(NavigationTarget target)
    {
        _ = RunClockAsync(_cts.Token);
        return Task.CompletedTask;
    }

    protected override void OnLanguageChanged() => RefreshText();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RefreshText()
    {
        OnPropertyChanged(nameof(Greeting));
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(ModuleNotice));
        OnPropertyChanged(nameof(PhaseBadge));
    }

    /// <summary>Keeps the greeting and date current while the dashboard stays open (e.g. past midnight).</summary>
    private async Task RunClockAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(true))
            {
                OnPropertyChanged(nameof(Greeting));
                OnPropertyChanged(nameof(DateText));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
