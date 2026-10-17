using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;
using static Engine.Domain.Tests.GraphTestData;

namespace Engine.Domain.Tests;

public class GraphStructureOperationsTests
{
    // ---------- add-node ----------

    [Fact]
    public void AddNode_AppendsAnIsolatedNode_KeepingEveryExistingIndexAndEdge()
    {
        var graph = Undirected(3, (0, 1));

        var result = new AddNodeOperation().Execute(graph);

        var grown = Assert.IsType<Graph>(result.NewState);
        Assert.Equal(4, grown.NodeCount);
        Assert.Equal(0, grown.Degree(3));
        Assert.Equal(Edges(graph), Edges(grown));
        Assert.Equal(new NodesAddedResult(FirstNode: 3, Count: 1), result.Value);
    }

    [Fact]
    public void AddNode_Several_ReportsTheFirstNewIndex()
    {
        var result = new AddNodeOperation(count: 3).Execute(Graph.Empty(2));

        Assert.Equal(5, Assert.IsType<Graph>(result.NewState).NodeCount);
        Assert.Equal(new NodesAddedResult(2, 3), result.Value);
    }

    [Fact]
    public void AddNode_NonPositiveCount_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AddNodeOperation(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)] // must be rejected, not wrap around the limit check
    public void AddNode_BeyondTheNodeLimit_IsRejected(int count)
    {
        var atLimit = Graph.Empty(DatasetLimits.MaxCells);
        Assert.Throws<ArgumentException>(() => new AddNodeOperation(count).Execute(atLimit));
    }

    // ---------- set-edge-weight ----------

    [Fact]
    public void SetEdgeWeight_ChangesOnlyTheDirectedEdgeNamed()
    {
        var graph = new Graph(2, [(0, 1, 1.0), (1, 0, 1.0)]);

        var result = new SetEdgeWeightOperation(0, 1, 5.0).Execute(graph);

        Assert.Equal([(0, 1, 5.0), (1, 0, 1.0)], Edges(Assert.IsType<Graph>(result.NewState)));
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void SetEdgeWeight_Symmetric_UpdatesTheReverseEdgeToo()
    {
        var graph = new Graph(2, [(0, 1, 1.0), (1, 0, 1.0)]);

        var result = new SetEdgeWeightOperation(0, 1, 5.0, symmetric: true).Execute(graph);

        Assert.Equal([(0, 1, 5.0), (1, 0, 5.0)], Edges(Assert.IsType<Graph>(result.NewState)));
        Assert.Equal(2, result.Value);
    }

    [Fact]
    public void SetEdgeWeight_UpdatesEveryParallelCopy()
    {
        var graph = new Graph(2, [(0, 1, 1.0), (0, 1, 2.0)]);

        var result = new SetEdgeWeightOperation(0, 1, 7.0).Execute(graph);

        Assert.Equal([(0, 1, 7.0), (0, 1, 7.0)], Edges(Assert.IsType<Graph>(result.NewState)));
        Assert.Equal(2, result.Value);
    }

    [Fact]
    public void SetEdgeWeight_NoSuchEdge_ReturnsZero_AndTheGraphUntouched()
    {
        var graph = new Graph(3, [(0, 1, 1.0)]);

        var result = new SetEdgeWeightOperation(1, 2, 9.0).Execute(graph);

        Assert.Equal(0, result.Value);
        Assert.Same(graph, result.NewState);
    }

    [Fact]
    public void SetEdgeWeight_DoesNotMutateTheInput()
    {
        var graph = new Graph(2, [(0, 1, 1.0)]);
        new SetEdgeWeightOperation(0, 1, 9.0).Execute(graph);
        Assert.Equal(1.0, graph.GetWeight(0, 1));
    }

    [Fact]
    public void SetEdgeWeight_NodeOutOfRange_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new SetEdgeWeightOperation(0, 9, 1.0).Execute(Graph.Empty(3)));
    }

    [Fact]
    public void SetEdgeWeight_NonFiniteWeight_IsRejected()
    {
        var graph = new Graph(2, [(0, 1, 1.0)]);
        Assert.Throws<ArgumentException>(() => new SetEdgeWeightOperation(0, 1, double.PositiveInfinity).Execute(graph));
    }

    // ---------- clear-edges ----------

    [Fact]
    public void ClearEdges_RemovesEveryEdge_KeepsTheNodes()
    {
        var graph = Undirected(3, (0, 1), (1, 2)); // 4 directed edges

        var result = new ClearEdgesOperation().Execute(graph);

        var cleared = Assert.IsType<Graph>(result.NewState);
        Assert.Equal(3, cleared.NodeCount);
        Assert.Equal(0, cleared.EdgeCount);
        Assert.Equal(new ClearGraphResult(EdgesRemoved: 4, NodesRemoved: 0), result.Value);
    }

    [Fact]
    public void ClearEdges_WithRemoveNodes_EmptiesTheGraphCompletely()
    {
        var result = new ClearEdgesOperation(removeNodes: true).Execute(Undirected(3, (0, 1), (1, 2)));

        Assert.Equal(0, Assert.IsType<Graph>(result.NewState).NodeCount);
        Assert.Equal(new ClearGraphResult(4, 3), result.Value);
    }

    // ---------- reverse-edges ----------

    [Fact]
    public void ReverseEdges_FlipsEveryEdge_KeepingWeights()
    {
        var graph = new Graph(3, [(0, 1, 2.0), (1, 2, 3.0)]);

        var result = new ReverseEdgesOperation().Execute(graph);

        Assert.Equal([(1, 0, 2.0), (2, 1, 3.0)], Edges(Assert.IsType<Graph>(result.NewState)));
    }

    [Fact]
    public void ReverseEdges_LeavesAnUndirectedGraphUnchanged()
    {
        var graph = Undirected(3, (0, 1), (1, 2));

        var result = new ReverseEdgesOperation().Execute(graph);

        Assert.Equal(Edges(graph), Edges(Assert.IsType<Graph>(result.NewState)));
    }

    [Fact]
    public void ReverseEdges_Twice_RestoresTheOriginal()
    {
        var graph = new Graph(3, [(0, 1, 2.0), (1, 2, 3.0), (2, 0, 4.0)]);

        var once = Assert.IsType<Graph>(new ReverseEdgesOperation().Execute(graph).NewState);
        var twice = Assert.IsType<Graph>(new ReverseEdgesOperation().Execute(once).NewState);

        Assert.Equal(Edges(graph), Edges(twice));
    }

    // ---------- subgraph ----------

    [Fact]
    public void Subgraph_KeepsOnlyEdgesBetweenKeptNodes_AndRenumbers()
    {
        var graph = Undirected(5, (0, 1), (1, 2), (2, 3), (3, 4));

        var result = new SubgraphOperation([1, 2, 4]).Execute(graph);

        var sub = Assert.IsType<Graph>(result.NewState);
        Assert.Equal(3, sub.NodeCount);
        Assert.Equal([(0, 1, 1.0), (1, 0, 1.0)], Edges(sub)); // only 1-2 survives, as new nodes 0 and 1
        var mapping = Assert.IsType<SubgraphResult>(result.Value);
        Assert.Equal(new[] { 1, 2, 4 }, mapping.OriginalNodes);
        Assert.Equal(2, mapping.EdgesKept);
    }

    [Fact]
    public void Subgraph_IgnoresOrderAndDuplicatesInTheRequest()
    {
        var graph = Undirected(5, (0, 1), (1, 2));

        var messy = new SubgraphOperation([2, 1, 1, 0]).Execute(graph);
        var clean = new SubgraphOperation([0, 1, 2]).Execute(graph);

        Assert.Equal(Edges(Assert.IsType<Graph>(clean.NewState)), Edges(Assert.IsType<Graph>(messy.NewState)));
    }

    [Fact]
    public void Subgraph_EmptySelection_GivesAnEmptyGraph()
    {
        var result = new SubgraphOperation([]).Execute(Undirected(3, (0, 1)));
        Assert.Equal(0, Assert.IsType<Graph>(result.NewState).NodeCount);
    }

    [Fact]
    public void Subgraph_NodeOutOfRange_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new SubgraphOperation([0, 9]).Execute(Graph.Empty(3)));
    }

    // ---------- remove-graph-node ----------

    [Fact]
    public void RemoveGraphNode_ShiftsHigherIndicesDown_AndDropsItsEdges()
    {
        var graph = Undirected(4, (0, 1), (1, 2), (2, 3)); // a path 0-1-2-3

        var result = new RemoveGraphNodeOperation(1).Execute(graph);

        var updated = Assert.IsType<Graph>(result.NewState);
        Assert.Equal(3, updated.NodeCount);
        Assert.Equal([(1, 2, 1.0), (2, 1, 1.0)], Edges(updated)); // old 2-3 is now 1-2
        var info = Assert.IsType<RemoveGraphNodeResult>(result.Value);
        Assert.Equal(1, info.RemovedNode);
        Assert.Equal(4, info.EdgesRemoved); // 0>1, 1>0, 1>2, 2>1
        Assert.Equal(3, info.NewNodeCount);
        Assert.Equal(new[] { 0, 2, 3 }, info.OldOfNew);
        Assert.False(info.Truncated);
    }

    [Fact]
    public void RemoveGraphNode_LastNode_NeedsNoRenumbering()
    {
        var graph = Undirected(3, (0, 1), (1, 2));

        var result = new RemoveGraphNodeOperation(2).Execute(graph);

        Assert.Equal([(0, 1, 1.0), (1, 0, 1.0)], Edges(Assert.IsType<Graph>(result.NewState)));
        Assert.Equal(new[] { 0, 1 }, Assert.IsType<RemoveGraphNodeResult>(result.Value).OldOfNew);
    }

    [Fact]
    public void RemoveGraphNode_KeepsWeightsAndDirection()
    {
        var graph = new Graph(3, [(0, 2, 7.5), (2, 1, 3.0), (1, 1, 9.0)]);

        var result = new RemoveGraphNodeOperation(0).Execute(graph);

        Assert.Equal([(0, 0, 9.0), (1, 0, 3.0)], Edges(Assert.IsType<Graph>(result.NewState)));
        Assert.Equal(1, Assert.IsType<RemoveGraphNodeResult>(result.Value).EdgesRemoved);
    }

    [Fact]
    public void RemoveGraphNode_SelfLoopOnTheNode_CountsOnce()
    {
        var graph = new Graph(2, [(0, 0, 1.0), (0, 1, 1.0)]);

        var result = new RemoveGraphNodeOperation(0).Execute(graph);

        Assert.Equal(2, Assert.IsType<RemoveGraphNodeResult>(result.Value).EdgesRemoved);
        Assert.Equal(0, Assert.IsType<Graph>(result.NewState).EdgeCount);
    }

    [Fact]
    public void RemoveGraphNode_OnlyNode_LeavesAnEmptyGraph()
    {
        var result = new RemoveGraphNodeOperation(0).Execute(Graph.Empty(1));

        Assert.Equal(0, Assert.IsType<Graph>(result.NewState).NodeCount);
        Assert.Empty(Assert.IsType<RemoveGraphNodeResult>(result.Value).OldOfNew);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void RemoveGraphNode_OutOfRange_Throws(int node)
    {
        Assert.Throws<ArgumentException>(() => new RemoveGraphNodeOperation(node).Execute(Graph.Empty(3)));
    }

    [Fact]
    public void RemoveGraphNode_DoesNotMutateInput()
    {
        var graph = Undirected(3, (0, 1), (1, 2));

        new RemoveGraphNodeOperation(1).Execute(graph);

        Assert.Equal(3, graph.NodeCount);
        Assert.Equal(4, graph.EdgeCount);
    }

    [Fact]
    public void RemoveGraphNode_TheMappingMatchesTheRule()
    {
        var graph = Graph.Empty(6);

        var info = Assert.IsType<RemoveGraphNodeResult>(new RemoveGraphNodeOperation(3).Execute(graph).Value);

        Assert.Equal(new[] { 0, 1, 2, 4, 5 }, info.OldOfNew);
    }
}
