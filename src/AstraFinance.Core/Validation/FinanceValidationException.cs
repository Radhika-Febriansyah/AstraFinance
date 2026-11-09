namespace AstraFinance.Core.Validation;

/// <summary>
/// A single validation failure. <see cref="Code"/> is a stable identifier that is also the
/// localization key suffix ("Validation.{Code}") used by the UI.
/// </summary>
public sealed record ValidationError(string Field, string Code);

/// <summary>Raised when a financial operation is rejected. Nothing has been written when this is thrown.</summary>
public sealed class FinanceValidationException : Exception
{
    public FinanceValidationException(IReadOnlyList<ValidationError> errors)
        : base("Validation failed: " + string.Join(", ", errors?.Select(e => $"{e.Field}:{e.Code}") ?? []))
    {
        Errors = errors ?? [];
    }

    public FinanceValidationException(string field, string code)
        : this([new ValidationError(field, code)])
    {
    }

    public FinanceValidationException()
        : this([])
    {
    }

    public FinanceValidationException(string message)
        : base(message)
    {
        Errors = [];
    }

    public FinanceValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [];
    }

    public IReadOnlyList<ValidationError> Errors { get; }

    public bool Has(string code) => Errors.Any(e => e.Code == code);
}

/// <summary>Stable validation codes. Each has a "Validation.{Code}" string in both languages.</summary>
public static class ValidationCodes
{
    public const string Required = "Required";
    public const string TooLong = "TooLong";
    public const string AmountMustBePositive = "AmountMustBePositive";
    public const string AmountTooLarge = "AmountTooLarge";
    public const string AmountMustNotBeZero = "AmountMustNotBeZero";
    public const string AccountNotFound = "AccountNotFound";
    public const string AccountInactive = "AccountInactive";
    public const string CategoryNotFound = "CategoryNotFound";
    public const string CategoryInactive = "CategoryInactive";
    public const string CategoryKindMismatch = "CategoryKindMismatch";
    public const string CategoryNotAllowedForTransfer = "CategoryNotAllowedForTransfer";
    public const string TransferSameAccount = "TransferSameAccount";
    public const string TransferDestinationRequired = "TransferDestinationRequired";
    public const string DestinationOnlyForTransfer = "DestinationOnlyForTransfer";
    public const string FeeOnlyForTransfer = "FeeOnlyForTransfer";
    public const string TransactionNotFound = "TransactionNotFound";
    public const string FeeEditedThroughTransfer = "FeeEditedThroughTransfer";
    public const string BalanceUnchanged = "BalanceUnchanged";
    public const string DuplicateName = "DuplicateName";
    public const string Overflow = "Overflow";
    public const string AllocationExceedsAvailable = "AllocationExceedsAvailable";
    public const string ReleaseExceedsAllocated = "ReleaseExceedsAllocated";
    public const string CurrencyLocked = "CurrencyLocked";
    public const string PinFormat = "PinFormat";
    public const string PinMismatch = "PinMismatch";
    public const string PinIncorrect = "PinIncorrect";
    public const string DirectoryNotWritable = "DirectoryNotWritable";
}
