using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Money;
using AstraFinance.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Infrastructure.Services;

public sealed class SettingsStore : ISettingsStore
{
    private readonly IDbContextFactory<AstraDbContext> _factory;
    private readonly IClock _clock;

    public SettingsStore(IDbContextFactory<AstraDbContext> factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await UpsertAsync(db, key, value, cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<RegionalFormat> GetRegionalFormatAsync(CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var values = await db.Settings.AsNoTracking()
                .Where(s => s.Key == SettingKeys.BaseCurrency || s.Key == SettingKeys.NumberFormat || s.Key == SettingKeys.DateFormat)
                .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken).ConfigureAwait(false);

            var defaults = RegionalFormat.Default;
            return new RegionalFormat(
                values.TryGetValue(SettingKeys.BaseCurrency, out var code) ? CurrencyInfo.FromCode(code) : defaults.Currency,
                ParseEnum(values.GetValueOrDefault(SettingKeys.NumberFormat), defaults.NumberStyle),
                ParseEnum(values.GetValueOrDefault(SettingKeys.DateFormat), defaults.DateStyle));
        }
    }

    public async Task SetRegionalFormatAsync(RegionalFormat format, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(format);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await UpsertAsync(db, SettingKeys.BaseCurrency, format.Currency.Code, cancellationToken).ConfigureAwait(false);
            await UpsertAsync(db, SettingKeys.NumberFormat, format.NumberStyle.ToString(), cancellationToken).ConfigureAwait(false);
            await UpsertAsync(db, SettingKeys.DateFormat, format.DateStyle.ToString(), cancellationToken).ConfigureAwait(false);

            // All three keys are written in one SaveChanges, i.e. one atomic SQLite transaction.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static T ParseEnum<T>(string? value, T fallback)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;

    private async Task UpsertAsync(AstraDbContext db, string key, string value, CancellationToken ct)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct).ConfigureAwait(false);
        var now = _clock.UtcNow.UtcDateTime;
        if (setting is null)
        {
            db.Settings.Add(new AppSetting { Key = key, Value = value, UpdatedAtUtc = now });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAtUtc = now;
        }
    }
}
