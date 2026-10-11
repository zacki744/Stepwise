using Engine.Domain.Capabilities;
using Engine.Domain.Datasets;
using Xunit;

namespace Engine.Domain.Tests;

public class TraversalTests
{
    // This method knows nothing about Matrix or Graph specifically —
    // only that TNode/TDataset satisfy ITraversable<TNode>. This is
    // the real proof that the capability interface generalizes.
    private static int CountReachable<TDataset, TNode>(TDataset dataset, TNode start)
        where TDataset : ITraversable<TNode>
        where TNode : notnull
    {
        var visited = new HashSet<TNode> { start };
        var queue = new Queue<TNode>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in dataset.GetNeighbors(current))
            {
                if (visited.Add(neighbor))
                    queue.Enqueue(neighbor);
            }
        }

        return visited.Count;
    }

    [Fact]
    public void Matrix_AllCellsReachableFromCorner()
    {
        var matrix = new Matrix(rows: 3, columns: 4);

        int reachable = CountReachable<Matrix, Cell>(matrix, new Cell(0, 0));

        Assert.Equal(12, reachable); // 3x4 grid, fully connected by adjacency
    }

    [Fact]
    public void Graph_ReachabilityRespectsEdges()
    {
        var graph = new Graph(nodeCount: 4, edges: [(0, 1), (1, 2)]); // 3 unreachable from node 3

        int reachableFrom0 = CountReachable<Graph, int>(graph, 0);
        int reachableFrom3 = CountReachable<Graph, int>(graph, 3);

        Assert.Equal(3, reachableFrom0); // 0, 1, 2
        Assert.Equal(1, reachableFrom3); // just itself, no outgoing edges
    }
}