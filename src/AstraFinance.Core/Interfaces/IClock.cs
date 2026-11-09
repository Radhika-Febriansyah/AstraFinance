namespace AstraFinance.Core.Interfaces;

/// <summary>Abstracts the system clock so time-dependent logic is testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Current local date and time of the Windows user.</summary>
    DateTime LocalNow { get; }

    DateOnly Today => DateOnly.FromDateTime(LocalNow);
}
