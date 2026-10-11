using Engine.Domain;
using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Engine.Session;
using Xunit;

namespace Engine.Session.Tests;

public class SessionManagerExecuteTests
{
    [Fact]
    public void Execute_Success_CommitsNewStateToSession()
    {
        var manager = new SessionManager();
        var session = manager.Create(new Matrix(2, 2));

        var outcome = manager.Execute(session.Id, new FillMatrixOperation());

        Assert.True(outcome.Succeeded);
        var committed = Assert.IsType<Matrix>(session.Dataset);
        Assert.Equal(2, committed[0, 0]); // the committed dataset reflects the operation
    }

    [Fact]
    public void Execute_Success_ReturnsTraceFromOperation()
    {
        var manager = new SessionManager();
        var session = manager.Create(new Matrix(2, 2));

        var outcome = manager.Execute(session.Id, new FillMatrixOperation());

        Assert.Equal(4, outcome.Trace.Count);
    }
    [Fact]
    public void Execute_QueryOperation_DoesNotChangeCommittedDataset()
    {
        var manager = new SessionManager();
        var graph = new Graph(nodeCount: 3, edges: [(0, 1)]);
        var session = manager.Create(graph);

        var outcome = manager.Execute(session.Id, new ReachabilityQuery(startNode: 0));

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Value);
        Assert.Same(graph, session.Dataset); // exact same reference — no commit happened at all
    }
}
