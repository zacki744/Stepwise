using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class ReachabilityQueryTests
{
    [Fact]
    public void Execute_ReturnsCorrectReachableCount()
    {
        var graph = new Graph(nodeCount: 4, edges: [(0, 1), (1, 2)]);
        var query = new ReachabilityQuery(startNode: 0);

        var result = query.Execute(graph);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Value); // 0, 1, 2
        Assert.Null(result.NewState); // confirms ProducesValue never sets NewState
    }

    [Fact]
    public void Execute_UnreachableNode_ReturnsOne()
    {
        var graph = new Graph(nodeCount: 4, edges: [(0, 1), (1, 2)]);
        var query = new ReachabilityQuery(startNode: 3);

        var result = query.Execute(graph);

        Assert.Equal(1, result.Value); // just itself
    }
}