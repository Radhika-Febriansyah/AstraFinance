using AstraFinance.Core.Money;

namespace AstraFinance.Core.Interfaces;

public enum PinCheckOutcome
{
    Success = 0,
    Incorrect = 1,
    CoolingDown = 2,
    NotConfigured = 3,
}

/// <summary>Result of a PIN check. <see cref="RetryAt"/> is set while entry is cooling down.</summary>
public sealed record PinCheckResult(PinCheckOutcome Outcome, int RemainingFreeAttempts, DateTimeOffset? RetryAt);

/// <summary>
/// Application access PIN. This is an access control only — it does not encrypt the database.
/// </summary>
public interface IPinService
{
    Task<bool> HasPinAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the PIN (format: 4–8 digits). Hashing runs off the calling thread.</summary>
    Task SetPinAsync(string pin, CancellationToken cancellationToken = default);

    /// <summary>Verifies the PIN, enforcing and persisting the failed-attempt cooldown.</summary>
    Task<PinCheckResult> VerifyAsync(string pin, CancellationToken cancellationToken = default);

    /// <summary>Current cooldown end, if entry is blocked right now.</summary>
    Task<DateTimeOffset?> GetCooldownEndAsync(CancellationToken cancellationToken = default);

    /// <summary>Changes the PIN after verifying the current one (counts as an attempt).</summary>
    Task<PinCheckResult> ChangePinAsync(string currentPin, string newPin, CancellationToken cancellationToken = default);
}

/// <summary>Onboarding wizard steps, in order. Values are persisted; never renumber.</summary>
public enum OnboardingStep
{
    Welcome = 0,
    Regional = 1,
    FirstAccount = 2,
    Pin = 3,
    Backup = 4,
    Completion = 5,
}

/// <summary>Persisted onboarding progress.</summary>
/// <param name="IsCompleted">Setup finished; the wizard never shows again.</param>
/// <param name="CurrentStep">Step to resume at.</param>
/// <param name="FirstAccountId">Account created in the wizard (prevents duplicates when resuming).</param>
public sealed record OnboardingState(bool IsCompleted, OnboardingStep CurrentStep, Guid? FirstAccountId);

public interface IOnboardingService
{
    Task<OnboardingState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Records that the user may resume at <paramref name="resumeAt"/>.</summary>
    Task SaveStepAsync(OnboardingStep resumeAt, CancellationToken cancellationToken = default);

    Task SetFirstAccountAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>Marks setup complete. Requires an account and a PIN.</summary>
    Task CompleteAsync(CancellationToken cancellationToken = default);
}

/// <summary>Application-wide regional format (base currency, number and date styles).</summary>
public interface IRegionalFormatService
{
    RegionalFormat Current { get; }

    event EventHandler? Changed;

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the base currency may still change (no accounts exist yet).</summary>
    Task<bool> CanChangeCurrencyAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the format. Throws if the currency changes while accounts exist.</summary>
    Task UpdateAsync(RegionalFormat format, CancellationToken cancellationToken = default);

    string FormatMoney(long minorUnits, bool includeSymbol = true);

    string FormatDate(DateOnly value);
}

/// <summary>Automatic backup frequency. Values are persisted; never renumber.</summary>
public enum BackupSchedule
{
    Off = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
}

public sealed record BackupSettings(string Directory, BackupSchedule Schedule, int RetentionCount)
{
    public const int DefaultRetention = 10;
}

/// <summary>A backup file found in the backup directory.</summary>
public sealed record BackupFileInfo(string Path, DateTimeOffset CreatedAt, long SizeBytes, bool IsAutomatic);

public interface IBackupService
{
    Task<BackupSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates that the directory is writable, then saves the settings.</summary>
    Task SaveSettingsAsync(BackupSettings settings, CancellationToken cancellationToken = default);

    /// <summary>True if a file can be created in <paramref name="directory"/>.</summary>
    bool IsWritableDirectory(string directory);

    /// <summary>Creates a verified backup now and applies retention to automatic backups.</summary>
    Task<BackupFileInfo> CreateBackupAsync(bool isAutomatic, CancellationToken cancellationToken = default);

    /// <summary>Creates an automatic backup when the schedule says one is due. Returns null when not due.</summary>
    Task<BackupFileInfo?> RunScheduledBackupIfDueAsync(CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLastBackupAsync(CancellationToken cancellationToken = default);
}
