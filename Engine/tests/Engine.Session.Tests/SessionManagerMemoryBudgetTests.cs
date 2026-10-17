using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Engine.Session;
using Xunit;

namespace Engine.Session.Tests;

public class SessionManagerMemoryBudgetTests
{
    // A 100x100 matrix is estimated at 64 + 4 * 10,000 bytes.
    private static Matrix Grid() => new(100, 100);
    private static readonly long GridBytes = Grid().EstimatedBytes;

    [Fact]
    public void Create_BeyondTheDatasetBudget_IsRefused_EvenWithSessionsToSpare()
    {
        var manager = new SessionManager(maxSessions: 10, maxTotalDatasetBytes: GridBytes * 2);
        manager.Create(Grid());
        manager.Create(Grid());

        var error = Assert.Throws<SessionCapacityException>(() => manager.Create(Grid()));

        Assert.Contains("dataset", error.Message);
        Assert.Equal(2, manager.ActiveSessionCount);
    }

    [Fact]
    public void Evicting_ReturnsItsShareOfTheBudget()
    {
        var manager = new SessionManager(maxSessions: 10, maxTotalDatasetBytes: GridBytes);
        var first = manager.Create(Grid());
        Assert.Throws<SessionCapacityException>(() => manager.Create(Grid()));

        manager.Evict(first.Id);

        manager.Create(Grid()); // fits again
        Assert.Equal(GridBytes, manager.TotalDatasetBytes);
    }

    [Fact]
    public void Replacing_OwnSession_DoesNotCountTheOldDatasetTwice()
    {
        var manager = new SessionManager(maxSessions: 10, maxTotalDatasetBytes: GridBytes);
        var first = manager.Create(Grid());

        var second = manager.Create(Grid(), replacing: first.Id);

        Assert.Null(manager.TryGet(first.Id));
        Assert.NotNull(manager.TryGet(second.Id));
        Assert.Equal(GridBytes, manager.TotalDatasetBytes);
    }

    [Fact]
    public void RefusedReplacement_KeepsTheOldSession()
    {
        var manager = new SessionManager(maxSessions: 10, maxTotalDatasetBytes: GridBytes);
        var first = manager.Create(Grid());

        Assert.Throws<SessionCapacityException>(() => manager.Create(new Matrix(200, 200), replacing: first.Id));

        Assert.NotNull(manager.TryGet(first.Id));
    }

    [Fact]
    public void Execute_ThatWouldGrowPastTheBudget_FailsAndLeavesTheDatasetUntouched()
    {
        var graph = Graph.Empty(10);
        // One undirected connection adds two directed edges: 2 * 12 bytes more than the empty graph.
        var manager = new SessionManager(maxSessions: 10, maxTotalDatasetBytes: graph.EstimatedBytes + 23);
        var session = manager.Create(graph);

        var outcome = manager.Execute(session.Id, new AddConnectionOperation(0, 1));

        Assert.False(outcome.Succeeded);
        Assert.Contains("memory budget", outcome.FailureReason);
        Assert.Same(graph, manager.TryGetDataset(session.Id));
        Assert.Equal(graph.EstimatedBytes, manager.TotalDatasetBytes);
    }

    [Fact]
    public void Execute_ThatFitsTheBudget_UpdatesTheChargedSize()
    {
        var graph = Graph.Empty(10);
        var manager = new SessionManager(maxSessions: 10, maxTotalDatasetBytes: graph.EstimatedBytes + 24);
        var session = manager.Create(graph);

        var outcome = manager.Execute(session.Id, new AddConnectionOperation(0, 1));

        Assert.True(outcome.Succeeded);
        Assert.Equal(graph.EstimatedBytes + 24, manager.TotalDatasetBytes);
    }

    [Fact]
    public void Estimates_GrowWithTheData()
    {
        Assert.True(new Matrix(10, 10).EstimatedBytes > new Matrix(2, 2).EstimatedBytes);
        Assert.True(new Graph(5, new (int, int, double)[] { (0, 1, 1.0) }).EstimatedBytes > Graph.Empty(5).EstimatedBytes);
        Assert.True(Tree.FromValues([1, 2, 3]).EstimatedBytes > Tree.Empty().EstimatedBytes);
    }
}
