namespace Mailgunner.Internal;

/// <summary>
/// A seedable source of uniform random doubles in <c>[0, 1)</c> used to jitter the computed backoff.
/// Abstracted so tests can inject a deterministic, seeded sequence and assert the wait schedule.
/// </summary>
internal interface IRetryRandom
{
    /// <summary>Returns the next random double in the range <c>[0, 1)</c>.</summary>
    /// <returns>A uniform random value in <c>[0, 1)</c>.</returns>
    double NextDouble();
}

/// <summary>
/// The default <see cref="IRetryRandom"/>: a thread-safe source of process-wide randomness. Used in
/// production so retries from concurrent callers are desynchronized.
/// </summary>
internal sealed class DefaultRetryRandom : IRetryRandom
{
#if NET8_0_OR_GREATER
    /// <inheritdoc />
    public double NextDouble() => Random.Shared.NextDouble();
#else
    [ThreadStatic]
    private static Random? _random;

    /// <inheritdoc />
    public double NextDouble() => (_random ??= new Random()).NextDouble();
#endif
}
