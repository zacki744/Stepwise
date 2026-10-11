using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class FindArticulationPointsOperationTests
{
    [Fact]
    public void Execute_LineTopology_MiddleNodesAreArticulationPoints()
    {
        // 0-1-2-3: removing node 1 or 2 splits the network; 0 and 3 are leaves
        var graph = new Graph(4, [(0, 1, 1.0), (1, 0, 1.0), (1, 2, 1.0), (2, 1, 1.0), (2, 3, 1.0), (3, 2, 1.0)]);

        var result = new FindArticulationPointsOperation().Execute(graph);

        var points = Assert.IsType<List<int>>(result.Value);
        Assert.Contains(1, points);
        Assert.Contains(2, points);
        Assert.DoesNotContain(0, points);
        Assert.DoesNotContain(3, points);
    }

    [Fact]
    public void Execute_Triangle_HasNoArticulationPoints()
    {
        // Fully connected 3-cycle: removing any single node leaves the other two still connected
        var graph = new Graph(3, [(0, 1, 1.0), (1, 0, 1.0), (1, 2, 1.0), (2, 1, 1.0), (0, 2, 1.0), (2, 0, 1.0)]);

        var result = new FindArticulationPointsOperation().Execute(graph);

        Assert.Empty((List<int>)result.Value!);
    }

    [Fact]
    public void Execute_NeverTouchesCommittedState()
    {
        var result = new FindArticulationPointsOperation().Execute(Graph.Empty(2));
        Assert.Null(result.NewState);
    }
}