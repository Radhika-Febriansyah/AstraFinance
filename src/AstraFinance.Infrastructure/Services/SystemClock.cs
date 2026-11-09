using AstraFinance.Core.Interfaces;

namespace AstraFinance.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTime LocalNow => DateTime.Now;
}
