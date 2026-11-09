using AstraFinance.Core.Enums;
using AstraFinance.Core.Rules;
using AstraFinance.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Infrastructure.Services;

/// <summary>
/// Aggregates account activity in SQL. Soft-deleted records are excluded by the global query
/// filters. Each transfer row is counted once as "out" (source) and once as "in" (destination).
/// </summary>
internal static class BalanceQueries
{
    public static async Task<Dictionary<Guid, BalanceComponents>> ComponentsAsync(
        AstraDbContext db, IReadOnlyCollection<Guid>? accountIds, CancellationToken ct)
    {
        var accounts = db.Accounts.AsNoTracking();
        if (accountIds is not null)
        {
            accounts = accounts.Where(a => accountIds.Contains(a.Id));
        }

        var opening = await accounts.Select(a => new { a.Id, a.OpeningBalance }).ToListAsync(ct).ConfigureAwait(false);
        var outgoing = db.Transactions.AsNoTracking();
        var incoming = db.Transactions.AsNoTracking().Where(t => t.Type == TransactionType.Transfer);
        var adjustmentRows = db.BalanceAdjustments.AsNoTracking();
        if (accountIds is not null)
        {
            outgoing = outgoing.Where(t => accountIds.Contains(t.AccountId));
            incoming = incoming.Where(t => accountIds.Contains(t.DestinationAccountId!.Value));
            adjustmentRows = adjustmentRows.Where(a => accountIds.Contains(a.AccountId));
        }

        var income = await SumBy(outgoing.Where(t => t.Type == TransactionType.Income), ct).ConfigureAwait(false);
        var expense = await SumBy(outgoing.Where(t => t.Type == TransactionType.Expense), ct).ConfigureAwait(false);
        var transfersOut = await SumBy(outgoing.Where(t => t.Type == TransactionType.Transfer), ct).ConfigureAwait(false);
        var transfersIn = (await incoming
                .GroupBy(t => t.DestinationAccountId!.Value)
                .Select(g => new { Id = g.Key, Sum = g.Sum(t => t.Amount) })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(x => x.Id, x => x.Sum);
        var adjustments = (await adjustmentRows
                .GroupBy(a => a.AccountId)
                .Select(g => new { Id = g.Key, Sum = g.Sum(a => a.Amount) })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(x => x.Id, x => x.Sum);

        return opening.ToDictionary(
            a => a.Id,
            a => new BalanceComponents(
                a.OpeningBalance,
                income.GetValueOrDefault(a.Id),
                expense.GetValueOrDefault(a.Id),
                transfersIn.GetValueOrDefault(a.Id),
                transfersOut.GetValueOrDefault(a.Id),
                adjustments.GetValueOrDefault(a.Id)));
    }

    public static async Task<long> BalanceAsync(AstraDbContext db, Guid accountId, CancellationToken ct)
    {
        var components = await ComponentsAsync(db, [accountId], ct).ConfigureAwait(false);
        return components.TryGetValue(accountId, out var c)
            ? BalanceCalculator.Compute(c)
            : throw new KeyNotFoundException("Account not found.");
    }

    private static async Task<Dictionary<Guid, long>> SumBy(IQueryable<Core.Entities.Transaction> source, CancellationToken ct) =>
        (await source.GroupBy(t => t.AccountId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(t => t.Amount) })
            .ToListAsync(ct).ConfigureAwait(false))
        .ToDictionary(x => x.Id, x => x.Sum);
}
