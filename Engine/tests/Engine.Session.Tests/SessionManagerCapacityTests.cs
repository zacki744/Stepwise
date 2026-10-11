using Engine.Domain.Datasets;
using Engine.Session;
using Xunit;

namespace Engine.Session.Tests;

public class SessionManagerCapacityTests
{
    [Fact]
    public void Create_AtCapacity_Throws()
    {
        var manager = new SessionManager(maxSessions: 2);
        manager.Create(new Matrix(1, 1));
        manager.Create(new Matrix(1, 1));

        Assert.Throws<SessionCapacityException>(() => manager.Create(new Matrix(1, 1)));
    }

    [Fact]
    public void Create_AfterEviction_ReusesTheFreedSlot()
    {
        var manager = new SessionManager(maxSessions: 1);
        var first = manager.Create(new Matrix(1, 1));
        manager.Evict(first.Id);

        var second = manager.Create(new Matrix(1, 1), replacing: first.Id);                       // was Create("matrix", default, ...)


        Assert.NotNull(manager.TryGet(second.Id));
    }

    [Fact]
    public void Create_ReplacingOwnSession_AtCapacity_SwapsInPlace()
    {
        var manager = new SessionManager(maxSessions: 1);
        var first = manager.Create(new Matrix(1, 1));

        var second = manager.Create(new Matrix(1, 1), replacing: first.Id);
        Assert.Null(manager.TryGet(first.Id));
        Assert.NotNull(manager.TryGet(second.Id));
        Assert.Equal(1, manager.ActiveSessionCount);
    }

    [Fact]
    public void Create_ReplacingUnknownSession_GrantsNoExtraSlot()
    {
        var manager = new SessionManager(maxSessions: 1);
        manager.Create(new Matrix(1, 1));

        Assert.Throws<SessionCapacityException>(() =>
            manager.Create(new Matrix(1, 1), replacing: Guid.NewGuid()));  
    }

    [Fact]
    public async Task Create_ConcurrentAtCapacity_NeverExceedsMax()
    {
        var manager = new SessionManager(maxSessions: 10);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            try { manager.Create(new Matrix(1, 1)); return true; }
            catch (SessionCapacityException) { return false; }
        })));

        Assert.Equal(10, outcomes.Count(ok => ok));
        Assert.Equal(10, manager.ActiveSessionCount);
    }
}