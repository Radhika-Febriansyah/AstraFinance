namespace AstraFinance.App.Services;

/// <summary>
/// Lets the first occurrence of an error through and suppresses identical repeats for a while. A UI error raised
/// during layout repeats on every layout pass; without this, each repeat would be logged and shown again.
/// </summary>
public sealed class RepeatedErrorFilter
{
    private readonly TimeSpan _window;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, DateTimeOffset> _lastSeen = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public RepeatedErrorFilter(TimeSpan window, Func<DateTimeOffset>? now = null)
    {
        _window = window;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>True when the exception should be reported (first time, or after the quiet window).</summary>
    public bool ShouldReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var key = Signature(exception);
        var now = _now();
        lock (_lock)
        {
            if (_lastSeen.TryGetValue(key, out var last) && now - last < _window)
            {
                return false;
            }

            _lastSeen[key] = now;
            if (_lastSeen.Count > 100)
            {
                // Keep the table small; old entries no longer suppress anything anyway.
                foreach (var stale in _lastSeen.Where(p => now - p.Value >= _window).Select(p => p.Key).ToList())
                {
                    _lastSeen.Remove(stale);
                }
            }

            return true;
        }
    }

    private static string Signature(Exception exception)
    {
        var inner = exception;
        while (inner.InnerException is not null)
        {
            inner = inner.InnerException;
        }

        return $"{exception.GetType().FullName}|{inner.GetType().FullName}|{inner.Message}|{exception.TargetSite?.Name}";
    }
}
