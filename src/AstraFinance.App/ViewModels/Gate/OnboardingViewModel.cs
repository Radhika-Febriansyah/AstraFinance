using System.Collections.ObjectModel;
using AstraFinance.App.Services;
using AstraFinance.App.ViewModels.Dialogs;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Money;
using AstraFinance.Core.Security;
using AstraFinance.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Gate;

public sealed partial class OnboardingStepItem : ObservableObject
{
    private readonly ILocalizationService _localization;

    public OnboardingStepItem(OnboardingStep step, ILocalizationService localization)
    {
        Step = step;
        _localization = localization;
    }

    public OnboardingStep Step { get; }

    public int Number => (int)Step + 1;

    public string Title => _localization.GetString($"Onboarding.Step.{Step}");

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    public partial bool IsDone { get; set; }

    public void RefreshText() => OnPropertyChanged(nameof(Title));
}

/// <summary>
/// First-launch setup wizard. Each completed step is persisted, so an interrupted setup resumes
/// at the right place without creating duplicate data. The Dashboard is unreachable until done.
/// </summary>
public sealed partial class OnboardingViewModel : ValidatableViewModel, IDisposable
{
    private readonly IPreferencesService _preferences;
    private readonly ILocalizationService _localization;
    private readonly IRegionalFormatService _regional;
    private readonly IAccountService _accounts;
    private readonly IOnboardingService _onboarding;
    private readonly IPinService _pin;
    private readonly IBackupService _backup;
    private readonly IFolderPicker _folderPicker;
    private readonly Func<AccountEditorViewModel> _editorFactory;
    private readonly DateOnly _today;
    private Func<Task> _onCompleted = () => Task.CompletedTask;
    private Guid? _firstAccountId;

    public OnboardingViewModel(
        IPreferencesService preferences,
        ILocalizationService localization,
        IRegionalFormatService regional,
        IAccountService accounts,
        IOnboardingService onboarding,
        IPinService pin,
        IBackupService backup,
        IFolderPicker folderPicker,
        Func<AccountEditorViewModel> editorFactory,
        IClock clock)
    {
        _preferences = preferences;
        _localization = localization;
        _regional = regional;
        _accounts = accounts;
        _onboarding = onboarding;
        _pin = pin;
        _backup = backup;
        _folderPicker = folderPicker;
        _editorFactory = editorFactory;
        _today = clock.Today;

        Steps = new(Enum.GetValues<OnboardingStep>().Select(s => new OnboardingStepItem(s, localization)));

        LanguageOptions =
        [
            new(AppLanguage.Indonesian, () => "Bahasa Indonesia", "Icon.Globe", SetLanguage),
            new(AppLanguage.English, () => "English", "Icon.Globe", SetLanguage),
        ];
        foreach (var option in LanguageOptions)
        {
            option.SetSelectedSilently(option.Value == preferences.Current.Language);
        }

        CurrencyOptions = CurrencyInfo.Supported.Select(c => new SelectOption<CurrencyInfo>(c, $"{c.Code} ({c.Symbol})")).ToList();
        NumberFormatOptions = Enum.GetValues<NumberFormatStyle>()
            .Select(s => new SelectOption<NumberFormatStyle>(s, MoneyFormatter.Format(123_456_789, new RegionalFormat(CurrencyInfo.Usd, s, DateFormatStyle.DayMonthYear), includeSymbol: false)))
            .ToList();
        DateFormatOptions = Enum.GetValues<DateFormatStyle>()
            .Select(s => new SelectOption<DateFormatStyle>(s, new RegionalFormat(CurrencyInfo.Idr, NumberFormatStyle.DotGroupCommaDecimal, s).FormatDate(_today)))
            .ToList();

        ScheduleOptions = Enum.GetValues<BackupSchedule>()
            .Select(s => new ChoiceOption<BackupSchedule>(s, () => _localization.GetString($"BackupSchedule.{s}"), null, v => SelectedSchedule = v))
            .ToList();

        _localization.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<OnboardingStepItem> Steps { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstStep), nameof(IsLastStep), nameof(NextText))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    public partial OnboardingStep CurrentStep { get; private set; }

    public bool IsFirstStep => CurrentStep == OnboardingStep.Welcome;

    public bool IsLastStep => CurrentStep == OnboardingStep.Completion;

    public string NextText => _localization.GetString(IsLastStep ? "Onboarding.Finish" : "Onboarding.Next");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    public partial bool IsBusy { get; private set; }

    // ---- Step 1: language
    public IReadOnlyList<ChoiceOption<AppLanguage>> LanguageOptions { get; }

    // ---- Step 2: regional
    public IReadOnlyList<SelectOption<CurrencyInfo>> CurrencyOptions { get; }

    public IReadOnlyList<SelectOption<NumberFormatStyle>> NumberFormatOptions { get; }

    public IReadOnlyList<SelectOption<DateFormatStyle>> DateFormatOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegionalPreview))]
    public partial SelectOption<CurrencyInfo>? SelectedCurrency { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegionalPreview))]
    public partial SelectOption<NumberFormatStyle>? SelectedNumberFormat { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegionalPreview))]
    public partial SelectOption<DateFormatStyle>? SelectedDateFormat { get; set; }

    [ObservableProperty]
    public partial bool CanChangeCurrency { get; private set; } = true;

    public string RegionalPreview
    {
        get
        {
            var format = BuildFormat();
            return $"{MoneyFormatter.Format(MoneyFormatter.SampleAmount(format.Currency), format)}  ·  {format.FormatDate(_today)}";
        }
    }

    // ---- Step 3: first account
    [ObservableProperty]
    public partial AccountEditorViewModel? AccountEditor { get; private set; }

    // ---- Step 4: PIN
    [ObservableProperty]
    public partial string Pin { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPin { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasExistingPin { get; private set; }

    // ---- Step 5: backup
    [ObservableProperty]
    public partial string BackupDirectory { get; set; } = string.Empty;

    public IReadOnlyList<ChoiceOption<BackupSchedule>> ScheduleOptions { get; }

    [ObservableProperty]
    public partial BackupSchedule SelectedSchedule { get; set; } = BackupSchedule.Weekly;

    // ---- Step 6: summary
    [ObservableProperty]
    public partial string SummaryAccount { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SummaryBackup { get; private set; } = string.Empty;

    public async Task InitializeAsync(OnboardingState state, Func<Task> onCompleted)
    {
        ArgumentNullException.ThrowIfNull(state);
        _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
        _firstAccountId = state.FirstAccountId;

        var format = _regional.Current;
        SelectedCurrency = CurrencyOptions.First(o => o.Value == format.Currency);
        SelectedNumberFormat = NumberFormatOptions.First(o => o.Value == format.NumberStyle);
        SelectedDateFormat = DateFormatOptions.First(o => o.Value == format.DateStyle);
        CanChangeCurrency = await _regional.CanChangeCurrencyAsync().ConfigureAwait(true);

        HasExistingPin = await _pin.HasPinAsync().ConfigureAwait(true);

        var backup = await _backup.GetSettingsAsync().ConfigureAwait(true);
        BackupDirectory = backup.Directory;
        SelectedSchedule = backup.Schedule;
        SyncScheduleOptions();

        await GoToAsync(state.CurrentStep).ConfigureAwait(true);
    }

    public void Dispose() => _localization.LanguageChanged -= OnLanguageChanged;

    [RelayCommand]
    private void BrowseBackupFolder()
    {
        var chosen = _folderPicker.PickFolder(_localization.GetString("Onboarding.Backup.Choose"), BackupDirectory);
        if (chosen is not null)
        {
            BackupDirectory = chosen;
            SetError(nameof(BackupDirectory), null);
        }
    }

    private bool CanGoBack() => !IsBusy && CurrentStep > OnboardingStep.Welcome;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task BackAsync() => GoToAsync(CurrentStep - 1);

    private bool CanGoNext() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        IsBusy = true;
        ClearAllErrors();
        try
        {
            var ok = CurrentStep switch
            {
                OnboardingStep.Welcome => true,
                OnboardingStep.Regional => await SaveRegionalAsync().ConfigureAwait(true),
                OnboardingStep.FirstAccount => await SaveAccountAsync().ConfigureAwait(true),
                OnboardingStep.Pin => await SavePinAsync().ConfigureAwait(true),
                OnboardingStep.Backup => await SaveBackupAsync().ConfigureAwait(true),
                _ => await FinishAsync().ConfigureAwait(true),
            };

            if (ok && CurrentStep != OnboardingStep.Completion)
            {
                var next = CurrentStep + 1;
                await _onboarding.SaveStepAsync(next).ConfigureAwait(true);
                await GoToAsync(next).ConfigureAwait(true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task GoToAsync(OnboardingStep step)
    {
        if (step == OnboardingStep.FirstAccount && AccountEditor is null)
        {
            // Created on demand so it uses the language chosen in step 1.
            var editor = _editorFactory();
            if (_firstAccountId is { } id && await _accounts.GetAsync(id).ConfigureAwait(true) is { } existing)
            {
                editor.LoadForEdit(existing.Account);
            }
            else
            {
                editor.Name = _localization.GetString("AccountType.Cash");
            }

            AccountEditor = editor;
        }

        if (step == OnboardingStep.Completion)
        {
            await BuildSummaryAsync().ConfigureAwait(true);
        }

        CurrentStep = step;
        foreach (var item in Steps)
        {
            item.IsCurrent = item.Step == step;
            item.IsDone = item.Step < step;
        }
    }

    private RegionalFormat BuildFormat() => new(
        SelectedCurrency?.Value ?? CurrencyInfo.Default,
        SelectedNumberFormat?.Value ?? NumberFormatStyle.DotGroupCommaDecimal,
        SelectedDateFormat?.Value ?? DateFormatStyle.DayMonthYear);

    private async Task<bool> SaveRegionalAsync()
    {
        try
        {
            await _regional.UpdateAsync(BuildFormat()).ConfigureAwait(true);
            return true;
        }
        catch (FinanceValidationException ex)
        {
            ApplyValidation(ex, _localization, new Dictionary<string, string> { ["Currency"] = nameof(SelectedCurrency) });
            return false;
        }
    }

    private async Task<bool> SaveAccountAsync()
    {
        if (AccountEditor is null || !await AccountEditor.SubmitAsync().ConfigureAwait(true))
        {
            return false;
        }

        _firstAccountId = AccountEditor.SavedAccount!.Id;
        await _onboarding.SetFirstAccountAsync(_firstAccountId.Value).ConfigureAwait(true);

        // Amounts now exist in the chosen currency: it can no longer change.
        CanChangeCurrency = false;
        return true;
    }

    private async Task<bool> SavePinAsync()
    {
        if (HasExistingPin && Pin.Length == 0 && ConfirmPin.Length == 0)
        {
            return true;
        }

        if (!PinRules.IsValidFormat(Pin))
        {
            SetError(nameof(Pin), _localization.GetString("Validation.PinFormat"));
            return false;
        }

        if (Pin != ConfirmPin)
        {
            SetError(nameof(ConfirmPin), _localization.GetString("Validation.PinMismatch"));
            return false;
        }

        await _pin.SetPinAsync(Pin).ConfigureAwait(true);
        Pin = string.Empty;
        ConfirmPin = string.Empty;
        HasExistingPin = true;
        return true;
    }

    private async Task<bool> SaveBackupAsync()
    {
        var current = await _backup.GetSettingsAsync().ConfigureAwait(true);
        try
        {
            await _backup.SaveSettingsAsync(current with { Directory = BackupDirectory, Schedule = SelectedSchedule }).ConfigureAwait(true);
            return true;
        }
        catch (FinanceValidationException ex)
        {
            ApplyValidation(ex, _localization, new Dictionary<string, string> { ["Directory"] = nameof(BackupDirectory) });
            return false;
        }
    }

    private async Task<bool> FinishAsync()
    {
        await _onboarding.CompleteAsync().ConfigureAwait(true);
        await _onCompleted().ConfigureAwait(true);
        return true;
    }

    private async Task BuildSummaryAsync()
    {
        if (_firstAccountId is { } id && await _accounts.GetAsync(id).ConfigureAwait(true) is { } account)
        {
            SummaryAccount = $"{account.Account.Name} · {_regional.FormatMoney(account.Balance)}";
        }

        SummaryBackup = $"{_localization.GetString($"BackupSchedule.{SelectedSchedule}")} · {BackupDirectory}";
    }

    private void SetLanguage(AppLanguage language) =>
        _ = _preferences.UpdateAsync(p => p with { Language = language });

    private void SyncScheduleOptions()
    {
        foreach (var option in ScheduleOptions)
        {
            option.SetSelectedSilently(option.Value == SelectedSchedule);
        }
    }

    partial void OnPinChanged(string value) => SetError(nameof(Pin), null);

    partial void OnConfirmPinChanged(string value) => SetError(nameof(ConfirmPin), null);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var step in Steps)
        {
            step.RefreshText();
        }

        foreach (var option in ScheduleOptions)
        {
            option.RefreshText();
        }

        OnPropertyChanged(nameof(NextText));

        // An unsaved account form is rebuilt so its labels use the new language.
        if (_firstAccountId is null && CurrentStep != OnboardingStep.FirstAccount)
        {
            AccountEditor = null;
        }
    }
}
