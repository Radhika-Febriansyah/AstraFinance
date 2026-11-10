using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Interfaces;

// ============================== Security ==============================

/// <summary>User-configurable failed-PIN cooldown (Settings → Security).</summary>
/// <param name="FreeAttempts">Wrong PINs allowed before a cooldown starts (3–10).</param>
/// <param name="BaseCooldown">First cooldown; it doubles with each further wrong PIN (30 s – 15 min).</param>
public sealed record PinAttemptSettings(int FreeAttempts, TimeSpan BaseCooldown)
{
    public const int MinFreeAttempts = 3;
    public const int MaxFreeAttempts = 10;

    public static readonly TimeSpan MinCooldown = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxBaseCooldown = TimeSpan.FromMinutes(15);

    public static PinAttemptSettings Default { get; } = new(5, TimeSpan.FromSeconds(30));

    /// <summary>The longest cooldown: at least 15 minutes, and at least eight times the first cooldown.</summary>
    public TimeSpan MaxCooldown => TimeSpan.FromTicks(Math.Max(TimeSpan.FromMinutes(15).Ticks, BaseCooldown.Ticks * 8));

    public bool IsValid =>
        FreeAttempts is >= MinFreeAttempts and <= MaxFreeAttempts && BaseCooldown >= MinCooldown && BaseCooldown <= MaxBaseCooldown;
}

public interface IPinSecuritySettings
{
    Task<PinAttemptSettings> GetAttemptSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveAttemptSettingsAsync(PinAttemptSettings settings, CancellationToken cancellationToken = default);
}

// ============================== Backup history & restore ==============================

/// <summary>What kind of copy a backup file is (from its file name).</summary>
public enum BackupKind
{
    Manual = 0,
    Automatic = 1,
    PreMigration = 2,
    PreRestore = 3,
    External = 4,
}

/// <summary>A backup file listed in Settings → Data.</summary>
public sealed record BackupEntry(string Path, string FileName, DateTimeOffset CreatedAt, long SizeBytes, BackupKind Kind);

/// <summary>Result of checking a file before restoring it.</summary>
/// <param name="IsValid">True when the file can be restored.</param>
/// <param name="ProblemCode">Why it can't be restored (a localization key suffix), when invalid.</param>
/// <param name="NeedsUpgrade">The backup is from an older version; it is upgraded after restoring.</param>
public sealed record BackupInspection(
    bool IsValid,
    string? ProblemCode,
    int AccountCount,
    int TransactionCount,
    DateOnly? LastTransactionDate,
    bool OnboardingCompleted,
    bool NeedsUpgrade)
{
    public static BackupInspection Invalid(string code) => new(false, code, 0, 0, null, false, false);
}

public sealed record RestoreResult(string SafetyBackupPath, IReadOnlyList<string> AppliedMigrations);

public static class RestoreProblems
{
    public const string NotFound = "NotFound";
    public const string NotADatabase = "NotADatabase";
    public const string Damaged = "Damaged";
    public const string NotAstra = "NotAstra";
    public const string NewerVersion = "NewerVersion";
    public const string SameFile = "SameFile";
}

/// <summary>
/// Lists backups and restores one safely: validate → safety backup of the current data → replace the data
/// with SQLite's online backup API (all-or-nothing) → upgrade if needed → integrity check. A failure at any
/// point leaves the current data as it was. Callers must stop all other database activity first.
/// </summary>
public interface IRestoreService
{
    Task<IReadOnlyList<BackupEntry>> ListBackupsAsync(CancellationToken cancellationToken = default);

    Task<BackupInspection> InspectAsync(string path, CancellationToken cancellationToken = default);

    Task<RestoreResult> RestoreAsync(string path, CancellationToken cancellationToken = default);
}

// ============================== Export (JSON) ==============================

/// <summary>Writes all financial data to one readable JSON file (never the PIN or other security settings).</summary>
public interface IDataExportService
{
    /// <summary>Returns the number of records written.</summary>
    Task<int> ExportJsonAsync(string path, CancellationToken cancellationToken = default);
}

// ============================== Import (CSV) ==============================

/// <summary>How amounts are written in the CSV file.</summary>
public enum ImportNumberStyle
{
    /// <summary>Plain numbers, '.' as decimal separator (Astra's own export): 1500000, -12.34.</summary>
    Plain = 0,
    /// <summary>The app's regional number format (e.g. 1.500.000 or 1,500,000.00).</summary>
    Regional = 1,
}

/// <summary>Which CSV column holds which field. Indexes are zero-based; null = not present.</summary>
/// <param name="TypeColumn">Optional Income/Expense column; without it the sign of the amount decides.</param>
/// <param name="AccountColumn">Optional account-name column (Astra's own export); rows for other accounts are skipped.</param>
public sealed record ImportMapping(int DateColumn, int AmountColumn, int? DescriptionColumn, int? CategoryColumn, int? TypeColumn, int? NotesColumn, int? AccountColumn = null);

public sealed record ImportOptions(Guid AccountId, ImportMapping Mapping, DateFormatStyle DateStyle, ImportNumberStyle NumberStyle);

/// <summary>A parsed CSV file: header and data rows (raw text).</summary>
public sealed record CsvTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

public enum ImportRowStatus
{
    Ready = 0,
    /// <summary>Same date, type, amount and description already exist in the account (or earlier in the file).</summary>
    Duplicate = 1,
    /// <summary>Not imported: transfers, empty lines, Astra's own transfer-fee rows.</summary>
    Skipped = 2,
    Error = 3,
}

/// <summary>One CSV row after parsing. <paramref name="Message"/> explains duplicates, skips and errors.</summary>
public sealed record ImportRow(
    int LineNumber,
    ImportRowStatus Status,
    string? MessageCode,
    TransactionType Type,
    DateOnly Date,
    long Amount,
    string Description,
    Guid? CategoryId,
    string CategoryName,
    string? Notes);

public sealed record ImportPreview(IReadOnlyList<ImportRow> Rows)
{
    public int ReadyCount => Rows.Count(r => r.Status == ImportRowStatus.Ready);

    public int DuplicateCount => Rows.Count(r => r.Status == ImportRowStatus.Duplicate);

    public int SkippedCount => Rows.Count(r => r.Status == ImportRowStatus.Skipped);

    public int ErrorCount => Rows.Count(r => r.Status == ImportRowStatus.Error);
}

public static class ImportMessages
{
    public const string BadDate = "BadDate";
    public const string BadAmount = "BadAmount";
    public const string ZeroAmount = "ZeroAmount";
    public const string Transfer = "Transfer";
    public const string OtherAccount = "OtherAccount";
    public const string EmptyRow = "EmptyRow";
    public const string ExistsInAccount = "ExistsInAccount";
    public const string RepeatedInFile = "RepeatedInFile";
    public const string CategoryFallback = "CategoryFallback";
    public const string TooLong = "TooLong";
}

/// <summary>An import was refused; nothing was saved. <see cref="LineNumber"/> is the CSV line that failed validation.</summary>
public sealed class ImportFailedException : Exception
{
    public ImportFailedException()
    {
    }

    public ImportFailedException(string message)
        : base(message)
    {
    }

    public ImportFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ImportFailedException(int lineNumber, string code, Exception innerException)
        : base($"Line {lineNumber}: {code}", innerException)
    {
        LineNumber = lineNumber;
        Code = code;
    }

    public int LineNumber { get; }

    public string Code { get; } = string.Empty;
}

/// <summary>
/// Imports income and expense transactions from a CSV file into one account. Nothing is written until
/// <see cref="ImportAsync"/>; then every selected row is saved in one database transaction through the normal
/// transaction validation (all rows or none).
/// </summary>
public interface ITransactionImportService
{
    /// <summary>Reads a CSV file (comma or semicolon separated, UTF-8 with or without BOM, header row required).</summary>
    Task<CsvTable> ReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Best guess of the column mapping from the header names (English, Indonesian and Astra's own export).</summary>
    ImportMapping GuessMapping(IReadOnlyList<string> headers);

    Task<ImportPreview> PreviewAsync(CsvTable table, ImportOptions options, CancellationToken cancellationToken = default);

    /// <summary>Saves the given rows (normally the Ready ones, optionally duplicates too). Returns the number saved.</summary>
    Task<int> ImportAsync(Guid accountId, IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken = default);
}
