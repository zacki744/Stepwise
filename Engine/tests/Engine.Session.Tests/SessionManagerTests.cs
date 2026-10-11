using Engine.Domain;
using Engine.Domain.Datasets;
using Engine.Session;
using Xunit;

namespace Engine.Session.Tests;

public class SessionManagerTests
{
    [Fact]
    public void Create_ReturnsSessionWithGivenDataset()
    {
        var manager = new SessionManager();
        var matrix = new Matrix(2, 2);

        var session = manager.Create(matrix);

        Assert.Same(matrix, session.Dataset);
        Assert.NotEqual(Guid.Empty, session.Id);
    }

    [Fact]
    public void TryGet_ReturnsNullForUnknownSession()
    {
        var manager = new SessionManager();

        var result = manager.TryGet(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public void IsIdle_FalseImmediatelyAfterCreate()
    {
        var manager = new SessionManager();
        var session = manager.Create(new Matrix(1, 1));

        Assert.False(manager.IsIdle(session.Id, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void IsIdle_TrueWhenThresholdIsZero()
    {
        var manager = new SessionManager();
        var session = manager.Create(new Matrix(1, 1));

        // A zero threshold means "idle the instant it's not actively being touched"
        Assert.True(manager.IsIdle(session.Id, TimeSpan.Zero));
    }

    [Fact]
    public void Evict_RemovesSessionAndTryGetReturnsNull()
    {
        var manager = new SessionManager();
        var session = manager.Create(new Matrix(1, 1));

        var evicted = manager.Evict(session.Id);

        Assert.True(evicted);
        Assert.Null(manager.TryGet(session.Id));
        Assert.Equal(0, manager.ActiveSessionCount);
    }

    [Fact]
    public void Evict_UnknownSessionReturnsFalse()
    {
        var manager = new SessionManager();

        Assert.False(manager.Evict(Guid.NewGuid()));
    }
}