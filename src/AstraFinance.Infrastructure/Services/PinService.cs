using System.Globalization;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Security;
using AstraFinance.Core.Services;
using AstraFinance.Core.Validation;

namespace AstraFinance.Infrastructure.Services;

/// <summary>
/// Stores only a salted PBKDF2 hash of the PIN. Failed attempts and the cooldown end are
/// persisted, so restarting the app does not reset the cooldown. Hashing runs on the thread pool
/// to keep the UI responsive.
/// </summary>
public sealed class PinService : IPinService, IPinSecuritySettings, IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly IClock _clock;
    private readonly PinHasher _hasher;
    private readonly PinAttemptPolicy _policy;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PinService(ISettingsStore settings, IClock clock, PinHasher hasher, PinAttemptPolicy policy)
    {
        _settings = settings;
        _clock = clock;
        _hasher = hasher;
        _policy = policy;
    }

    public void Dispose() => _gate.Dispose();

    public async Task<bool> HasPinAsync(CancellationToken cancellationToken = default) =>
        !string.IsNullOrEmpty(await _settings.GetAsync(Phase3SettingKeys.PinHash, cancellationToken).ConfigureAwait(false));

    public async Task SetPinAsync(string pin, CancellationToken cancellationToken = default)
    {
        if (!PinRules.IsValidFormat(pin))
        {
            throw new FinanceValidationException("Pin", ValidationCodes.PinFormat);
        }

        var hash = await Task.Run(() => _hasher.Hash(pin), cancellationToken).ConfigureAwait(false);
        await _settings.SetAsync(Phase3SettingKeys.PinHash, hash, cancellationToken).ConfigureAwait(false);
        await ResetFailuresAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PinCheckResult> VerifyAsync(string pin, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = await _settings.GetAsync(Phase3SettingKeys.PinHash, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(stored))
            {
                return new PinCheckResult(PinCheckOutcome.NotConfigured, 0, null);
            }

            var policy = await GetPolicyAsync(cancellationToken).ConfigureAwait(false);
            var failures = await GetFailuresAsync(cancellationToken).ConfigureAwait(false);
            var cooldownEnd = await GetCooldownEndAsync(cancellationToken).ConfigureAwait(false);
            if (cooldownEnd is not null)
            {
                return new PinCheckResult(PinCheckOutcome.CoolingDown, 0, cooldownEnd);
            }

            var ok = pin is not null && await Task.Run(() => PinHasher.Verify(pin, stored), cancellationToken).ConfigureAwait(false);
            if (ok)
            {
                await ResetFailuresAsync(cancellationToken).ConfigureAwait(false);
                return new PinCheckResult(PinCheckOutcome.Success, policy.FreeAttempts, null);
            }

            failures++;
            await _settings.SetAsync(Phase3SettingKeys.PinFailedAttempts, failures.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            var cooldown = policy.CooldownAfter(failures);
            DateTimeOffset? retryAt = null;
            if (cooldown > TimeSpan.Zero)
            {
                retryAt = _clock.UtcNow + cooldown;
                await _settings.SetAsync(Phase3SettingKeys.PinCooldownUntil, retryAt.Value.ToString("O", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            }

            return new PinCheckResult(PinCheckOutcome.Incorrect, policy.RemainingFreeAttempts(failures), retryAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DateTimeOffset?> GetCooldownEndAsync(CancellationToken cancellationToken = default)
    {
        var text = await _settings.GetAsync(Phase3SettingKeys.PinCooldownUntil, cancellationToken).ConfigureAwait(false);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var until) && until > _clock.UtcNow
            ? until
            : null;
    }

    public async Task<PinCheckResult> ChangePinAsync(string currentPin, string newPin, CancellationToken cancellationToken = default)
    {
        if (!PinRules.IsValidFormat(newPin))
        {
            throw new FinanceValidationException("NewPin", ValidationCodes.PinFormat);
        }

        var check = await VerifyAsync(currentPin, cancellationToken).ConfigureAwait(false);
        if (check.Outcome == PinCheckOutcome.Success)
        {
            await SetPinAsync(newPin, cancellationToken).ConfigureAwait(false);
        }

        return check;
    }

    /// <summary>
    /// The cooldown settings chosen in Settings → Security; without a saved choice, the policy this service was
    /// created with (the app default, or a fast policy in tests).
    /// </summary>
    public async Task<PinAttemptSettings> GetAttemptSettingsAsync(CancellationToken cancellationToken = default)
    {
        var attempts = await _settings.GetAsync(SettingKeys.SecurityFreeAttempts, cancellationToken).ConfigureAwait(false);
        var seconds = await _settings.GetAsync(SettingKeys.SecurityBaseCooldownSeconds, cancellationToken).ConfigureAwait(false);
        if (int.TryParse(attempts, NumberStyles.None, CultureInfo.InvariantCulture, out var a)
            && int.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out var s))
        {
            var saved = new PinAttemptSettings(a, TimeSpan.FromSeconds(s));
            if (saved.IsValid)
            {
                return saved;
            }
        }

        return new PinAttemptSettings(_policy.FreeAttempts, _policy.BaseCooldown);
    }

    public async Task SaveAttemptSettingsAsync(PinAttemptSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid)
        {
            throw new FinanceValidationException("AttemptSettings", ValidationCodes.AttemptSettingsOutOfRange);
        }

        await _settings.SetAsync(SettingKeys.SecurityFreeAttempts, settings.FreeAttempts.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        await _settings.SetAsync(SettingKeys.SecurityBaseCooldownSeconds, ((int)settings.BaseCooldown.TotalSeconds).ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
    }

    private async Task<PinAttemptPolicy> GetPolicyAsync(CancellationToken ct)
    {
        var settings = await GetAttemptSettingsAsync(ct).ConfigureAwait(false);
        return settings.FreeAttempts == _policy.FreeAttempts && settings.BaseCooldown == _policy.BaseCooldown
            ? _policy
            : new PinAttemptPolicy(settings.FreeAttempts, settings.BaseCooldown, settings.MaxCooldown);
    }

    private async Task<int> GetFailuresAsync(CancellationToken ct) =>
        int.TryParse(await _settings.GetAsync(Phase3SettingKeys.PinFailedAttempts, ct).ConfigureAwait(false), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;

    private async Task ResetFailuresAsync(CancellationToken ct)
    {
        await _settings.SetAsync(Phase3SettingKeys.PinFailedAttempts, "0", ct).ConfigureAwait(false);
        await _settings.SetAsync(Phase3SettingKeys.PinCooldownUntil, string.Empty, ct).ConfigureAwait(false);
    }
}
