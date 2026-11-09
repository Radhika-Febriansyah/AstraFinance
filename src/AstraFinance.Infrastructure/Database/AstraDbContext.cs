using AstraFinance.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AstraFinance.Infrastructure.Database;

/// <summary>
/// EF Core context for the local SQLite database. Financial integrity is enforced twice: by the
/// services (with user-facing validation) and by database CHECK/foreign-key constraints.
/// Foreign keys never cascade: financial history is never deleted implicitly.
/// </summary>
public sealed class AstraDbContext : DbContext
{
    public AstraDbContext(DbContextOptions<AstraDbContext> options)
        : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<BalanceAdjustment> BalanceAdjustments => Set<BalanceAdjustment>();

    public DbSet<Budget> Budgets => Set<Budget>();

    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();

    public DbSet<GoalAllocation> GoalAllocations => Set<GoalAllocation>();

    public DbSet<Bill> Bills => Set<Bill>();

    public DbSet<BillPayment> BillPayments => Set<BillPayment>();

    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Audit timestamps are always UTC; restore the Kind when reading.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Account>(e =>
        {
            e.ToTable("Accounts");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.IconKey).HasMaxLength(64).IsRequired();
            e.Property(x => x.ColorHex).HasMaxLength(9).IsRequired();
            e.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("Categories", t => t.HasCheckConstraint("CK_Categories_Kind", "\"Kind\" IN (0, 1)"));
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.SystemKey).HasMaxLength(64);
            e.Property(x => x.IconKey).HasMaxLength(64).IsRequired();
            e.Property(x => x.ColorHex).HasMaxLength(9).IsRequired();
            e.HasIndex(x => x.SystemKey).IsUnique().HasFilter("\"SystemKey\" IS NOT NULL");
            e.HasIndex(x => new { x.Kind, x.IsActive });
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.ToTable("Transactions", t =>
            {
                t.HasCheckConstraint("CK_Transactions_AmountPositive", "\"Amount\" > 0");
                t.HasCheckConstraint(
                    "CK_Transactions_Shape",
                    "(\"Type\" = 2 AND \"DestinationAccountId\" IS NOT NULL AND \"DestinationAccountId\" <> \"AccountId\" AND \"CategoryId\" IS NULL)"
                    + " OR (\"Type\" IN (0, 1) AND \"DestinationAccountId\" IS NULL AND \"CategoryId\" IS NOT NULL)");
                t.HasCheckConstraint("CK_Transactions_DeletedAt", "(\"IsDeleted\" = 0) OR (\"DeletedAtUtc\" IS NOT NULL)");
            });
            e.Property(x => x.Description).HasMaxLength(200).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Transaction>().WithMany().HasForeignKey(x => x.ParentTransactionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Date);
            e.HasIndex(x => new { x.AccountId, x.Date });
            e.HasIndex(x => x.DestinationAccountId);
            e.HasIndex(x => new { x.CategoryId, x.Date });
            e.HasIndex(x => x.ParentTransactionId);
            e.HasIndex(x => x.IsDeleted);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<BalanceAdjustment>(e =>
        {
            e.ToTable("BalanceAdjustments", t =>
            {
                t.HasCheckConstraint("CK_BalanceAdjustments_NonZero", "\"Amount\" <> 0");
                t.HasCheckConstraint("CK_BalanceAdjustments_Reason", "length(trim(\"Reason\")) > 0");
            });
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.Date });
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Budget>(e =>
        {
            e.ToTable("Budgets", t =>
            {
                t.HasCheckConstraint("CK_Budgets_AmountPositive", "\"Amount\" > 0");
                t.HasCheckConstraint("CK_Budgets_Threshold", "\"WarningThresholdPercent\" IS NULL OR (\"WarningThresholdPercent\" BETWEEN 1 AND 100)");
            });
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CategoryId);
        });

        modelBuilder.Entity<SavingsGoal>(e =>
        {
            e.ToTable("SavingsGoals", t => t.HasCheckConstraint("CK_SavingsGoals_TargetPositive", "\"TargetAmount\" > 0"));
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.IconKey).HasMaxLength(64).IsRequired();
            e.Property(x => x.ColorHex).HasMaxLength(9).IsRequired();
            e.Property(x => x.ImagePath).HasMaxLength(1024);
        });

        modelBuilder.Entity<GoalAllocation>(e =>
        {
            e.ToTable("GoalAllocations", t => t.HasCheckConstraint("CK_GoalAllocations_NonZero", "\"Amount\" <> 0"));
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne<SavingsGoal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.GoalId);
            e.HasIndex(x => x.AccountId);
        });

        modelBuilder.Entity<Bill>(e =>
        {
            e.ToTable("Bills", t => t.HasCheckConstraint("CK_Bills_AmountPositive", "\"Amount\" > 0"));
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.PaymentAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillPayment>(e =>
        {
            e.ToTable("BillPayments");
            e.HasOne<Bill>().WithMany().HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Transaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);

            // One confirmed payment per bill occurrence: prevents duplicate payment records.
            e.HasIndex(x => new { x.BillId, x.DueDate }).IsUnique();
            e.HasIndex(x => x.TransactionId).IsUnique();
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("Settings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(128);
            e.Property(x => x.Value).HasMaxLength(4000).IsRequired();
        });
    }

    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }
}
