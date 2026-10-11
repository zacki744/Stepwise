using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class FindConnectedComponentsOperationTests
{
    [Fact]
    public void Execute_FullyConnectedGraph_ReportsExactlyOneComponent()
    {
        var graph = new Graph(3, [(0, 1, 1.0), (1, 0, 1.0), (1, 2, 1.0), (2, 1, 1.0)]);

        var result = new FindConnectedComponentsOperation().Execute(graph);

        var components = Assert.IsType<List<List<int>>>(result.Value);
        Assert.Single(components);
        Assert.Equal([0, 1, 2], components[0]);
    }

    [Fact]
    public void Execute_TwoSeparateIslands_ReportsTwoComponents()
    {
        var graph = new Graph(4, [(0, 1, 1.0), (1, 0, 1.0), (2, 3, 1.0), (3, 2, 1.0)]); // {0,1} and {2,3}, no link between

        var result = new FindConnectedComponentsOperation().Execute(graph);

        var components = Assert.IsType<List<List<int>>>(result.Value);
        Assert.Equal(2, components.Count);
        Assert.Equal([0, 1], components[0]);
        Assert.Equal([2, 3], components[1]);
    }

    [Fact]
    public void Execute_AllIsolatedNodes_EachIsItsOwnComponent()
    {
        var graph = Graph.Empty(3); // no edges at all

        var result = new FindConnectedComponentsOperation().Execute(graph);

        var components = Assert.IsType<List<List<int>>>(result.Value);
        Assert.Equal(3, components.Count);
    }
}