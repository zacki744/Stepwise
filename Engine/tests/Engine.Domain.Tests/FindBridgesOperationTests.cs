using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class FindBridgesOperationTests
{
    [Fact]
    public void Execute_LineTopology_EveryEdgeIsABridge()
    {
        var graph = new Graph(4, [(0, 1, 1.0), (1, 0, 1.0), (1, 2, 1.0), (2, 1, 1.0), (2, 3, 1.0), (3, 2, 1.0)]);
        var bridges = Assert.IsType<List<BridgeEdge>>(new FindBridgesOperation().Execute(graph).Value);
        Assert.Equal(3, bridges.Count);
    }

    [Fact]
    public void Execute_Triangle_HasNoBridges()
    {
        var graph = new Graph(3, [(0, 1, 1.0), (1, 0, 1.0), (1, 2, 1.0), (2, 1, 1.0), (0, 2, 1.0), (2, 0, 1.0)]);
        Assert.Empty((List<BridgeEdge>)new FindBridgesOperation().Execute(graph).Value!);
    }

    [Fact]
    public void Execute_TriangleWithDanglingEdge_OnlyTheDanglingEdgeIsABridge()
    {
        var graph = new Graph(4, [
            (0,1,1.0),(1,0,1.0),(1,2,1.0),(2,1,1.0),(0,2,1.0),(2,0,1.0),
            (2,3,1.0),(3,2,1.0)]);
        var bridges = Assert.IsType<List<BridgeEdge>>(new FindBridgesOperation().Execute(graph).Value);
        Assert.Equal(new[] { new BridgeEdge(2, 3) }, bridges);
    }

    [Fact]
    public void Execute_DuplicatedConnection_IsNotABridge()
    {
        // Two parallel connections between 0 and 1: removing one still leaves a path
        var graph = new Graph(2, [(0, 1, 1.0), (1, 0, 1.0), (0, 1, 1.0), (1, 0, 1.0)]);
        Assert.Empty((List<BridgeEdge>)new FindBridgesOperation().Execute(graph).Value!);
    }
}