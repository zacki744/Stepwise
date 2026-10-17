using Xunit;

namespace Engine.Gateway.Tests;

public class HardeningUnitTests
{
    // ---------- OriginPolicy ----------

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://app.example", true)]
    [InlineData("https://APP.example/", true)]
    [InlineData("https://app.example:8443", false)]
    [InlineData("http://app.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("https://app.example.evil.example", false)]
    [InlineData("null", false)]
    public void Origin_WithAList_MustMatchExactly(string? origin, bool expected)
    {
        Assert.Equal(expected, OriginPolicy.IsAllowed(origin, ["https://app.example"]));
    }

    [Theory]
    [InlineData("https://anything.example")]
    [InlineData("null")]
    public void Origin_WithAnEmptyList_IsNotChecked(string origin)
    {
        Assert.True(OriginPolicy.IsAllowed(origin, []));
    }

    // ---------- ConnectionLimiter ----------

    [Fact]
    public void Limiter_RefusesBeyondTheLimit_AndFreesASlotOnDispose()
    {
        var limiter = new ConnectionLimiter(2);

        var first = limiter.TryAcquire("a");
        var second = limiter.TryAcquire("a");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(limiter.TryAcquire("a"));

        first!.Dispose();

        Assert.NotNull(limiter.TryAcquire("a"));
    }

    [Fact]
    public void Limiter_CountsEachKeySeparately()
    {
        var limiter = new ConnectionLimiter(1);

        Assert.NotNull(limiter.TryAcquire("a"));
        Assert.NotNull(limiter.TryAcquire("b"));
        Assert.Null(limiter.TryAcquire("a"));
    }

    [Fact]
    public void Limiter_DisposingTwice_ReleasesOnlyOnce()
    {
        var limiter = new ConnectionLimiter(2);
        var keep = limiter.TryAcquire("a");
        var lease = limiter.TryAcquire("a");

        lease!.Dispose();
        lease.Dispose();

        Assert.Equal(1, limiter.ActiveFor("a"));
        GC.KeepAlive(keep);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Limiter_ZeroOrLess_MeansNoLimit(int limit)
    {
        var limiter = new ConnectionLimiter(limit);

        for (int i = 0; i < 1_000; i++) Assert.NotNull(limiter.TryAcquire("a"));
    }

    [Fact]
    public async Task Limiter_UnderContention_NeverExceedsTheLimit()
    {
        var limiter = new ConnectionLimiter(5);
        int granted = 0;

        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
        {
            if (limiter.TryAcquire("a") is not null) Interlocked.Increment(ref granted);
        })));

        Assert.Equal(5, granted);
        Assert.Equal(5, limiter.ActiveFor("a"));
    }

    // ---------- RecentRequestIds ----------

    [Fact]
    public void RecentRequestIds_RefusesARepeat()
    {
        var recent = new RecentRequestIds();
        var id = Guid.NewGuid();

        Assert.True(recent.TryRecord(id));
        Assert.False(recent.TryRecord(id));
        Assert.True(recent.TryRecord(Guid.NewGuid()));
    }

    [Fact]
    public void RecentRequestIds_ForgetsTheOldestBeyondItsCapacity()
    {
        var recent = new RecentRequestIds(capacity: 2);
        var first = Guid.NewGuid();
        recent.TryRecord(first);
        recent.TryRecord(Guid.NewGuid());
        recent.TryRecord(Guid.NewGuid()); // pushes `first` out

        Assert.True(recent.TryRecord(first));
    }
}
