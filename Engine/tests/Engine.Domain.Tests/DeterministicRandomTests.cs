using Engine.Domain;
using Xunit;

namespace Engine.Domain.Tests;

/// <summary>
/// Golden values: these must never change. They were produced by an independent implementation of
/// SplitMix64, so a failure here means saved operation logs would replay differently.
/// </summary>
public class DeterministicRandomTests
{
    [Theory]
    [InlineData(0, 0xe220a8397b1dcdafUL, 0x6e789e6aa1b965f4UL, 0x06c45d188009454fUL)] // matches the published SplitMix64 vector
    [InlineData(1, 0x910a2dec89025cc1UL, 0xbeeb8da1658eec67UL, 0xf893a2eefb32555eUL)]
    [InlineData(42, 0xbdd732262feb6e95UL, 0x28efe333b266f103UL, 0x47526757130f9f52UL)]
    [InlineData(-1, 0xe4d971771b652c20UL, 0xe99ff867dbf682c9UL, 0x382ff84cb27281e9UL)]
    public void NextUInt64_MatchesGoldenSequence(int seed, ulong first, ulong second, ulong third)
    {
        var random = new DeterministicRandom(seed);
        Assert.Equal(first, random.NextUInt64());
        Assert.Equal(second, random.NextUInt64());
        Assert.Equal(third, random.NextUInt64());
    }

    [Fact]
    public void Next_Bounded_MatchesGoldenSequence()
    {
        var random = new DeterministicRandom(42);
        Assert.Equal(new[] { 13, 91, 58, 64, 50, 62, 25, 8 }, Enumerable.Range(0, 8).Select(_ => random.Next(100)));
    }

    [Fact]
    public void NextInt64_MatchesGoldenSequence()
    {
        var random = new DeterministicRandom(7);
        Assert.Equal(
            new[] { 600892374487L, 372594955804L, 746815609346L },
            Enumerable.Range(0, 3).Select(_ => random.NextInt64(1_000_000_000_000L)));
    }

    [Fact]
    public void Next_Range_MatchesGoldenSequence()
    {
        var random = new DeterministicRandom(5);
        Assert.Equal(new[] { 5, 7, 8, 8, 4, 7 }, Enumerable.Range(0, 6).Select(_ => random.Next(3, 9)));
    }

    [Fact]
    public void SameSeed_SameSequence_DifferentSeed_DifferentSequence()
    {
        var a = new DeterministicRandom(99);
        var b = new DeterministicRandom(99);
        var c = new DeterministicRandom(100);
        Assert.Equal(a.NextUInt64(), b.NextUInt64());
        Assert.NotEqual(new DeterministicRandom(99).NextUInt64(), c.NextUInt64());
    }

    [Fact]
    public void Next_StaysInRange_AndCoversAllValues()
    {
        var random = new DeterministicRandom(3);
        var seen = new HashSet<int>();
        for (int i = 0; i < 2_000; i++)
        {
            int value = random.Next(7);
            Assert.InRange(value, 0, 6);
            seen.Add(value);
        }
        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public void Next_RoughlyUniform()
    {
        var random = new DeterministicRandom(11);
        var counts = new int[10];
        for (int i = 0; i < 100_000; i++) counts[random.Next(10)]++;
        Assert.All(counts, c => Assert.InRange(c, 9_500, 10_500)); // ~7 standard deviations of slack
    }

    [Fact]
    public void ZeroBound_ReturnsZero_NegativeBound_Throws()
    {
        var random = new DeterministicRandom(1);
        Assert.Equal(0, random.Next(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(5, 4));
    }

    [Fact]
    public void Range_HandlesFullIntSpan_WithoutOverflow()
    {
        var random = new DeterministicRandom(8);
        for (int i = 0; i < 100; i++)
            Assert.InRange(random.Next(int.MinValue, int.MaxValue), int.MinValue, int.MaxValue - 1);
    }
}
