using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Gate;

/// <summary>PIN lock screen shown at launch (after setup) and when the user locks the app.</summary>
public sealed partial class LockViewModel : ObservableObject, IDisposable
{
    private readonly IPinService _pin;
    private readonly ILocalizationService _localization;
    private readonly IClock _clock;
    private Func<Task> _onUnlocked = () => Task.CompletedTask;
    private CancellationTokenSource? _countdown;

    public LockViewModel(IPinService pin, ILocalizationService localization, IClock clock)
    {
        _pin = pin;
        _localization = localization;
        _clock = clock;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    public partial string Pin { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    public partial bool IsCoolingDown { get; private set; }

    /// <summary>Incremented to ask the view to refocus the PIN box.</summary>
    [ObservableProperty]
    public partial int FocusRequest { get; private set; }

    public async Task InitializeAsync(Func<Task> onUnlocked)
    {
        _onUnlocked = onUnlocked ?? throw new ArgumentNullException(nameof(onUnlocked));
        var cooldownEnd = await _pin.GetCooldownEndAsync().ConfigureAwait(true);
        if (cooldownEnd is not null)
        {
            StartCountdown(cooldownEnd.Value);
        }
    }

    public void Dispose()
    {
        _countdown?.Cancel();
        _countdown?.Dispose();
        _countdown = null;
    }

    private bool CanUnlock() => !IsBusy && !IsCoolingDown && PinRules.IsValidFormat(Pin);

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _pin.VerifyAsync(Pin).ConfigureAwait(true);
            Pin = string.Empty;
            switch (result.Outcome)
            {
                case PinCheckOutcome.Success:
                case PinCheckOutcome.NotConfigured:
                    await _onUnlocked().ConfigureAwait(true);
                    return;
                case PinCheckOutcome.CoolingDown:
                    StartCountdown(result.RetryAt!.Value);
                    break;
                default:
                    if (result.RetryAt is { } retryAt)
                    {
                        StartCountdown(retryAt);
                    }
                    else
                    {
                        ErrorMessage = _localization.Format("Lock.Incorrect", result.RemainingFreeAttempts);
                    }

                    break;
            }
        }
        finally
        {
            IsBusy = false;
            FocusRequest++;
        }
    }

    private void StartCountdown(DateTimeOffset until)
    {
        _countdown?.Cancel();
        _countdown?.Dispose();
        _countdown = new CancellationTokenSource();
        _ = RunCountdownAsync(until, _countdown.Token);
    }

    private async Task RunCountdownAsync(DateTimeOffset until, CancellationToken token)
    {
        IsCoolingDown = true;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            do
            {
                var remaining = until - _clock.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                ErrorMessage = _localization.Format("Lock.Cooldown", FormatRemaining(remaining));
            }
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(true));
        }
        catch (OperationCanceledException)
        {
            return;
        }

        IsCoolingDown = false;
        ErrorMessage = null;
        FocusRequest++;
    }

    private static string FormatRemaining(TimeSpan remaining) =>
        remaining.TotalMinutes >= 1
            ? $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}"
            : $"0:{Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds)):00}";
}
