using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;
using static Engine.Domain.Operations.MatrixToGraphOperation;

namespace Engine.Domain.Tests;

public class MatrixToGraphOperationTests
{
    [Fact]
    public void Execute_AllOpenCells_CreatesFullyConnectedGridGraph()
    {
        var matrix = new Matrix(2, 2); // all zeros — fully open

        var result = new MatrixToGraphOperation().Execute(matrix);

        var graph = Assert.IsType<Graph>(result.NewState);
        Assert.Equal(4, graph.NodeCount);
        // A 2x2 open grid: every node reachable from every other
        var visited = new HashSet<int> { 0 };
        var queue = new Queue<int>([0]);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var neighbor in graph.GetNeighbors(n))
                if (visited.Add(neighbor)) queue.Enqueue(neighbor);
        }
        Assert.Equal(4, visited.Count);
    }

    [Fact]
    public void Execute_WallCell_ExcludedEntirely_NotCountedAsNode()
    {
        var matrix = new Matrix(1, 3);
        matrix[0, 1] = 1; // wall in the middle

        var result = new MatrixToGraphOperation().Execute(matrix);

        var graph = Assert.IsType<Graph>(result.NewState);
        Assert.Equal(2, graph.NodeCount); // only the two open cells became nodes
    }

    [Fact]
    public void Execute_WallSplitsGrid_ProducesDisconnectedComponents()
    {
        var matrix = new Matrix(1, 3);
        matrix[0, 1] = 1; // wall splits (0,0) from (0,2)

        var result = new MatrixToGraphOperation().Execute(matrix);

        var graph = Assert.IsType<Graph>(result.NewState);
        Assert.Empty(graph.GetNeighbors(0)); // node 0 — (0,0) — has no path to the other side
    }

    [Fact]
    public void Execute_Value_MapsNodeIndexBackToGridPosition()
    {
        var matrix = new Matrix(1, 2); // two open cells: (0,0) -> node 0, (0,1) -> node 1

        var result = new MatrixToGraphOperation().Execute(matrix);

        var mapping = Assert.IsType<List<GridPosition>>(result.Value);
        Assert.Equal(new GridPosition(0, 0), mapping[0]);
        Assert.Equal(new GridPosition(0, 1), mapping[1]);
    }

    [Fact]
    public void Execute_NoOpenCells_ProducesEmptyGraph()
    {
        var matrix = new Matrix(2, 2);
        matrix[0, 0] = matrix[0, 1] = matrix[1, 0] = matrix[1, 1] = 1; // all walls

        var result = new MatrixToGraphOperation().Execute(matrix);

        var graph = Assert.IsType<Graph>(result.NewState);
        Assert.Equal(0, graph.NodeCount);
    }
}