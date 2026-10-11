using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class ShortestPathOperationTests
{
    [Fact]
    public void Execute_DirectPath_FindsIt()
    {
        var graph = new Graph(2, [(0, 1, 5.0)]);

        var result = new ShortestPathOperation(0, 1).Execute(graph);

        var path = Assert.IsType<ShortestPathResult>(result.Value);
        Assert.Equal([0, 1], path.Path);
        Assert.Equal(5.0, path.TotalDistance);
    }

    [Fact]
    public void Execute_PrefersCheaperIndirectPath_OverExpensiveDirectOne()
    {
        var graph = new Graph(3, [(0, 1, 1.0), (1, 2, 1.0), (0, 2, 10.0)]); // 0->1->2 costs 2, vs direct 10

        var result = new ShortestPathOperation(0, 2).Execute(graph);

        var path = Assert.IsType<ShortestPathResult>(result.Value);
        Assert.Equal([0, 1, 2], path.Path);
        Assert.Equal(2.0, path.TotalDistance);
    }

    [Fact]
    public void Execute_NoPathExists_ReturnsNullValue()
    {
        var graph = new Graph(3, [(0, 1, 1.0)]); // node 2 unreachable

        var result = new ShortestPathOperation(0, 2).Execute(graph);

        Assert.Null(result.Value);
        Assert.Null(result.NewState); // Query — never touches committed state
    }

    [Fact]
    public void Execute_SameStartAndTarget_ReturnsZeroDistanceSingleNodePath()
    {
        var graph = new Graph(2, [(0, 1, 1.0)]);

        var result = new ShortestPathOperation(0, 0).Execute(graph);

        var path = Assert.IsType<ShortestPathResult>(result.Value);
        Assert.Equal([0], path.Path);
        Assert.Equal(0.0, path.TotalDistance);
    }
}