using System.Globalization;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Validation;

namespace AstraFinance.Core.Services;

/// <summary>Setting keys introduced in Phase 3.</summary>
public static class Phase3SettingKeys
{
    public const string OnboardingCompleted = "Onboarding.Completed";
    public const string OnboardingStep = "Onboarding.Step";
    public const string OnboardingFirstAccount = "Onboarding.FirstAccountId";
    public const string PinHash = "Security.PinHash";
    public const string PinFailedAttempts = "Security.FailedAttempts";
    public const string PinCooldownUntil = "Security.CooldownUntilUtc";
    public const string BackupDirectory = "Backup.Directory";
    public const string BackupSchedule = "Backup.Schedule";
    public const string BackupRetention = "Backup.RetentionCount";
    public const string BackupLastUtc = "Backup.LastUtc";
}

/// <summary>Raised when an operation needs onboarding prerequisites that are missing.</summary>
public sealed class OnboardingIncompleteException : InvalidOperationException
{
    public OnboardingIncompleteException()
    {
    }

    public OnboardingIncompleteException(string message)
        : base(message)
    {
    }

    public OnboardingIncompleteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Persists wizard progress in the database so an interrupted setup resumes safely.</summary>
public sealed class OnboardingService : IOnboardingService
{
    private readonly ISettingsStore _settings;
    private readonly IAccountService _accounts;
    private readonly IPinService _pin;

    public OnboardingService(ISettingsStore settings, IAccountService accounts, IPinService pin)
    {
        _settings = settings;
        _accounts = accounts;
        _pin = pin;
    }

    public async Task<OnboardingState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var completed = await _settings.GetAsync(Phase3SettingKeys.OnboardingCompleted, cancellationToken).ConfigureAwait(false) == "true";
        var stepText = await _settings.GetAsync(Phase3SettingKeys.OnboardingStep, cancellationToken).ConfigureAwait(false);
        var step = int.TryParse(stepText, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && Enum.IsDefined((OnboardingStep)n)
            ? (OnboardingStep)n
            : OnboardingStep.Welcome;

        Guid? firstAccount = null;
        if (Guid.TryParse(await _settings.GetAsync(Phase3SettingKeys.OnboardingFirstAccount, cancellationToken).ConfigureAwait(false), out var id)
            && await _accounts.GetAsync(id, cancellationToken).ConfigureAwait(false) is not null)
        {
            firstAccount = id;
        }

        // Never resume past the account step if the account is missing (e.g. interrupted write).
        if (firstAccount is null && step > OnboardingStep.FirstAccount)
        {
            step = OnboardingStep.FirstAccount;
        }

        return new OnboardingState(completed, step, firstAccount);
    }

    public Task SaveStepAsync(OnboardingStep resumeAt, CancellationToken cancellationToken = default) =>
        _settings.SetAsync(Phase3SettingKeys.OnboardingStep, ((int)resumeAt).ToString(CultureInfo.InvariantCulture), cancellationToken);

    public Task SetFirstAccountAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        _settings.SetAsync(Phase3SettingKeys.OnboardingFirstAccount, accountId.ToString(), cancellationToken);

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await _accounts.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false);
        if (accounts.Count == 0)
        {
            throw new OnboardingIncompleteException("An account is required to finish setup.");
        }

        if (!await _pin.HasPinAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new OnboardingIncompleteException("A PIN is required to finish setup.");
        }

        await _settings.SetAsync(Phase3SettingKeys.OnboardingCompleted, "true", cancellationToken).ConfigureAwait(false);
        await SaveStepAsync(OnboardingStep.Completion, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Holds the regional format in memory for fast formatting and guards base-currency changes.</summary>
public sealed class RegionalFormatService : IRegionalFormatService
{
    private readonly ISettingsStore _settings;
    private readonly IAccountService _accounts;

    public RegionalFormatService(ISettingsStore settings, IAccountService accounts)
    {
        _settings = settings;
        _accounts = accounts;
    }

    public RegionalFormat Current { get; private set; } = RegionalFormat.Default;

    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Current = await _settings.GetRegionalFormatAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> CanChangeCurrencyAsync(CancellationToken cancellationToken = default) =>
        (await _accounts.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false)).Count == 0;

    public async Task UpdateAsync(RegionalFormat format, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(format);

        // Stored amounts are minor units of the base currency; changing it after accounts exist
        // would silently change the meaning of every stored amount.
        if (format.Currency != Current.Currency && !await CanChangeCurrencyAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new FinanceValidationException("Currency", ValidationCodes.CurrencyLocked);
        }

        await _settings.SetRegionalFormatAsync(format, cancellationToken).ConfigureAwait(false);
        Current = format;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string FormatMoney(long minorUnits, bool includeSymbol = true) => MoneyFormatter.Format(minorUnits, Current, includeSymbol);

    public string FormatDate(DateOnly value) => Current.FormatDate(value);
}

/// <summary>Global-search results for accounts (active and inactive).</summary>
public sealed class AccountSearchProvider : ISearchProvider
{
    private readonly IAccountService _accounts;
    private readonly IRegionalFormatService _format;

    public AccountSearchProvider(IAccountService accounts, IRegionalFormatService format)
    {
        _accounts = accounts;
        _format = format;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var normalized = SearchMatcher.Normalize(query);
        var accounts = await _accounts.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false);
        return accounts
            .Select(a => (Account: a, Score: SearchMatcher.Score(normalized, a.Account.Name)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Account.Account.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(maxResults)
            .Select(x => new SearchResult(
                SearchCategory.Accounts,
                x.Account.Account.Name,
                _format.FormatMoney(x.Account.Balance),
                x.Account.Account.IconKey,
                new NavigationTarget(AppPage.Accounts, x.Account.Account.Id),
                x.Score))
            .ToList();
    }
}

/// <summary>Suggested icon per account type.</summary>
public static class AccountDefaults
{
    public static string IconFor(AccountType type) => type switch
    {
        AccountType.Cash => "Icon.Banknote",
        AccountType.BankAccount => "Icon.Landmark",
        AccountType.EWallet => "Icon.Smartphone",
        AccountType.SavingsAccount => "Icon.PiggyBank",
        _ => "Icon.Accounts",
    };

    public static IReadOnlyList<string> Icons { get; } =
        ["Icon.Banknote", "Icon.Landmark", "Icon.Smartphone", "Icon.PiggyBank", "Icon.CreditCard", "Icon.Accounts", "Icon.Briefcase", "Icon.Store"];

    public static IReadOnlyList<string> Colors { get; } =
        ["#F4806B", "#F2B04A", "#3DBE8B", "#2EC4B0", "#6AA6F2", "#A78BFA", "#F07272", "#8A94A6"];
}
