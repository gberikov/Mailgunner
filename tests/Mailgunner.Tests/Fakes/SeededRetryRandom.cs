using Mailgunner.Internal;

namespace Mailgunner.Tests.Fakes;

/// <summary>
/// A deterministic <see cref="IRetryRandom"/> backed by a seeded <see cref="Random"/>, so jittered
/// backoff produces a reproducible wait schedule under test.
/// </summary>
internal sealed class SeededRetryRandom : IRetryRandom
{
    private readonly Random _random;

    /// <summary>Initializes a new instance of the <see cref="SeededRetryRandom"/> class.</summary>
    /// <param name="seed">The fixed seed driving the sequence.</param>
    public SeededRetryRandom(int seed) => _random = new Random(seed);

    /// <inheritdoc />
    public double NextDouble() => _random.NextDouble();
}
