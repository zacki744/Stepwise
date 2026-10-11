namespace Engine.Domain;

/// <summary>
/// Seeded random numbers whose sequence is fixed by this code alone (SplitMix64 with unbiased
/// bounded draws). Use this instead of <see cref="Random"/> anywhere a result must be reproducible:
/// the documentation for <c>System.Random</c> does not promise the same sequence across .NET
/// versions, and rehydration replays operations from a saved log, so a runtime upgrade must never
/// change what a given seed produces. Not thread-safe and not for security.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(int seed) => _state = unchecked((ulong)(long)seed);

    public ulong NextUInt64()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>A value in [0, maxExclusive). Like System.Random, 0 returns 0 and negatives throw.</summary>
    public long NextInt64(long maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxExclusive);
        if (maxExclusive == 0) return 0;

        // Rejection sampling: discard the few values that would make low results more likely.
        ulong bound = (ulong)maxExclusive;
        ulong threshold = unchecked(0UL - bound) % bound;
        while (true)
        {
            ulong r = NextUInt64();
            if (r >= threshold) return (long)(r % bound);
        }
    }

    /// <summary>A value in [0, maxExclusive).</summary>
    public int Next(int maxExclusive) => (int)NextInt64(maxExclusive);

    /// <summary>A value in [minInclusive, maxExclusive).</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive < minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must not be below minInclusive");
        return (int)(minInclusive + NextInt64((long)maxExclusive - minInclusive));
    }
}
