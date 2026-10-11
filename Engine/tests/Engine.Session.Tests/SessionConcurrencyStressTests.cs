using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Engine.Session;
using Xunit;

namespace Engine.Session.Tests;

public class SessionConcurrencyStressTests
{
    [Fact]
    public async Task ConcurrentExecute_SameSession_NoLostUpdates()
    {
        var manager = new SessionManager();
        var session = manager.Create(Graph.Empty(nodeCount: 50));

        // 40 concurrent AddConnection calls, each a distinct edge — before the
        // lock, concurrent read-then-commit races would silently drop some.
        var tasks = Enumerable.Range(0, 40).Select(i =>
            Task.Run(() => manager.Execute(session.Id, new AddConnectionOperation(i, i + 1))));

        await Task.WhenAll(tasks);

        var final = (Graph)session.Dataset;
        int totalEdges = Enumerable.Range(0, 50).Sum(n => final.GetNeighbors(n).Count()) / 2;
        Assert.Equal(40, totalEdges); // every single one must have committed
    }
}