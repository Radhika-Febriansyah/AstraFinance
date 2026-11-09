using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AstraFinance.Core.Security;

/// <summary>
/// Salted, slow PIN hashing with PBKDF2-HMAC-SHA256 (built into .NET, no third-party crypto).
/// Stored format: <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;salt base64&gt;$&lt;hash base64&gt;</c>.
/// The iteration count is stored with each hash, so the default can be raised later without
/// breaking existing PINs. The original PIN is never stored or logged.
/// </summary>
public sealed class PinHasher
{
    /// <summary>OWASP 2023+ recommendation for PBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 600_000;

    private const string Scheme = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public PinHasher(int iterations = DefaultIterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1_000);
        Iterations = iterations;
    }

    public int Iterations { get; }

    public string Hash(string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(pin, salt, Iterations);
        return string.Join('$', Scheme, Iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>Constant-time verification. Malformed stored values never verify.</summary>
    public static bool Verify(string pin, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations < 1_000)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Derive(pin, salt, iterations);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(string pin, byte[] salt, int iterations)
    {
        var bytes = Encoding.UTF8.GetBytes(pin);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(bytes, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

/// <summary>PIN format rules: 4–8 ASCII digits.</summary>
public static class PinRules
{
    public const int MinLength = 4;
    public const int MaxLength = 8;

    public static bool IsValidFormat(string? pin) =>
        pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);
}

/// <summary>
/// Failed-attempt cooldown: the first <see cref="FreeAttempts"/> failures are free; each further
/// failure locks entry for a doubling period, capped at <see cref="MaxCooldown"/>.
/// </summary>
public sealed record PinAttemptPolicy(int FreeAttempts, TimeSpan BaseCooldown, TimeSpan MaxCooldown)
{
    public static PinAttemptPolicy Default { get; } = new(5, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));

    /// <summary>Cooldown to apply after <paramref name="consecutiveFailures"/> failures (zero when none).</summary>
    public TimeSpan CooldownAfter(int consecutiveFailures)
    {
        if (consecutiveFailures < FreeAttempts)
        {
            return TimeSpan.Zero;
        }

        var doublings = Math.Min(consecutiveFailures - FreeAttempts, 20);
        var ticks = BaseCooldown.Ticks * (1L << doublings);
        return ticks <= 0 || ticks > MaxCooldown.Ticks ? MaxCooldown : TimeSpan.FromTicks(ticks);
    }

    public int RemainingFreeAttempts(int consecutiveFailures) => Math.Max(0, FreeAttempts - consecutiveFailures);
}
