using System.Text;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Infrastructure.Export;

namespace AstraFinance.Tests.Infrastructure;

public class TransactionsQueryAndExportTests
{
    [Fact]
    public async Task Summary_counts_all_matches_and_never_adds_transfers_to_income_or_expense()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank", 1_000_000);
        var wallet = await ctx.NewAccountAsync("Wallet");
        await ctx.IncomeAsync(bank, 500_000);
        await ctx.ExpenseAsync(bank, 20_000);
        await ctx.ExpenseAsync(wallet, 5_000);
        await ctx.TransferAsync(bank, wallet, 100_000, fee: 2_500);

        var all = await ctx.Transactions.SummarizeAsync(new TransactionQuery { PageSize = 1 });
        Assert.Equal(5, all.Count);                // income, 2 expenses, transfer, fee
        Assert.Equal(500_000, all.Income);
        Assert.Equal(27_500, all.Expense);          // includes the transfer fee

        var walletOnly = await ctx.Transactions.SummarizeAsync(new TransactionQuery { AccountIds = [wallet.Id] });
        Assert.Equal(2, walletOnly.Count);          // expense + incoming transfer
        Assert.Equal(5_000, walletOnly.Expense);
    }

    [Fact]
    public async Task Search_matches_resolved_categories_accounts_and_exact_amounts()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("Bank");
        var wallet = await ctx.NewAccountAsync("GoPay");
        var food = await ctx.CategoryAsync("Food");
        await ctx.ExpenseAsync(bank, 45_000, "Food", description: "Lunch");
        await ctx.ExpenseAsync(wallet, 12_000, "Transportation", description: "Ojek");
        await ctx.ExpenseAsync(bank, 99_000, "Shopping", description: "Shoes");

        Assert.Equal("Lunch", Assert.Single((await ctx.Transactions.QueryAsync(new TransactionQuery { Search = "makanan", SearchCategoryIds = [food.Id] })).Items).Description);
        Assert.Equal("Ojek", Assert.Single((await ctx.Transactions.QueryAsync(new TransactionQuery { Search = "gopay", SearchAccountIds = [wallet.Id] })).Items).Description);
        Assert.Equal("Shoes", Assert.Single((await ctx.Transactions.QueryAsync(new TransactionQuery { Search = "99.000", SearchAmount = 99_000 })).Items).Description);
        Assert.Empty((await ctx.Transactions.QueryAsync(new TransactionQuery { Search = "zzz" })).Items);
    }

    [Fact]
    public async Task Fee_lookup_returns_only_the_active_fee()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var a = await ctx.NewAccountAsync("A", 1_000);
        var b = await ctx.NewAccountAsync("B");
        var transfer = await ctx.TransferAsync(a, b, 100, fee: 5);
        Assert.Equal(transfer.Fee!.Id, (await ctx.Transactions.GetFeeAsync(transfer.Transaction.Id))!.Id);

        await ctx.Transactions.DeleteAsync(transfer.Transaction.Id);
        Assert.Null(await ctx.Transactions.GetFeeAsync(transfer.Transaction.Id));
    }

    [Theory]
    [InlineData(1_500_000, "1500000")]
    [InlineData(-25_000, "-25000")]
    [InlineData(0, "0")]
    public void Plain_amounts_for_rupiah(long amount, string expected) =>
        Assert.Equal(expected, MoneyFormatter.FormatPlain(amount, CurrencyInfo.Idr));

    [Theory]
    [InlineData(123_450, "1234.50")]
    [InlineData(-5, "-0.05")]
    public void Plain_amounts_for_fractional_currencies(long amount, string expected) =>
        Assert.Equal(expected, MoneyFormatter.FormatPlain(amount, CurrencyInfo.Usd));

    [Fact]
    public void Plain_amount_handles_the_minimum_value() =>
        Assert.Equal("-9223372036854775808", MoneyFormatter.FormatPlain(long.MinValue, CurrencyInfo.Idr));

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("-5 discount", "'-5 discount")]
    public void Csv_text_cells_are_quoted_and_protected_from_formulas(string value, string expected) =>
        Assert.Equal(expected, CsvWriter.Escape(value));

    [Fact]
    public void Csv_numeric_cells_keep_their_sign() =>
        Assert.Equal("-25000", CsvWriter.Escape(new CsvCell("-25000", IsNumeric: true)));

    [Fact]
    public async Task Csv_file_has_bom_crlf_and_all_rows()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "out.csv");
        await CsvWriter.WriteAsync(path, ["A", "B"], [["1", "x,y"], ["2", "z"]]);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("A,B\r\n1,\"x,y\"\r\n2,z\r\n", Encoding.UTF8.GetString(bytes[3..]));
        Assert.False(File.Exists(path + ".partial"));
    }
}
