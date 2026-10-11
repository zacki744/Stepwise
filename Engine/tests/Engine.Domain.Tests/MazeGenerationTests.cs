using Engine.Domain;
using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class MazeGenerationTests
{
    [Fact]
    public void Execute_ProducesSpanningTree_ExactlyNodeCountMinusOneEdges()
    {
        var op = new GenerateMazeOperation(width: 5, height: 5, seed: 42);

        var result = op.Execute(Graph.Empty(nodeCount: 25));

        Assert.Equal(24, CountUndirectedEdges(result.NewState)); // n nodes, n-1 edges — a tree
    }

    [Fact]
    public void Execute_ProducesFullyConnectedMaze()
    {
        var op = new GenerateMazeOperation(width: 5, height: 5, seed: 42);
        var result = op.Execute(Graph.Empty(nodeCount: 25));
        var maze = Assert.IsType<Graph>(result.NewState);

        Assert.Equal(25, BfsCount(maze, startNode: 0)); // every cell reachable — no isolated pockets
    }

    [Fact]
    public void Execute_SameSeed_ProducesIdenticalMaze()
    {
        var a = new GenerateMazeOperation(width: 4, height: 4, seed: 7).Execute(Graph.Empty(16));
        var b = new GenerateMazeOperation(width: 4, height: 4, seed: 7).Execute(Graph.Empty(16));

        Assert.Equal(EdgeSet(a.NewState), EdgeSet(b.NewState)); // determinism — required for rehydration replay
    }

    [Fact]
    public void Execute_DifferentSeeds_ProduceDifferentMazes()
    {
        var a = new GenerateMazeOperation(width: 5, height: 5, seed: 1).Execute(Graph.Empty(25));
        var b = new GenerateMazeOperation(width: 5, height: 5, seed: 2).Execute(Graph.Empty(25));

        Assert.NotEqual(EdgeSet(a.NewState), EdgeSet(b.NewState));
    }

    [Fact]
    public void Execute_EmitsOnePassageCarvedStepPerEdge()
    {
        var op = new GenerateMazeOperation(width: 5, height: 5, seed: 42);
        var result = op.Execute(Graph.Empty(25));

        Assert.Equal(24, result.Trace.Count(e => e.StepType == "PassageCarved"));
    }

    private static int CountUndirectedEdges(IDataset? dataset)
    {
        var graph = Assert.IsType<Graph>(dataset);
        int total = 0;
        for (int i = 0; i < graph.NodeCount; i++)
            total += graph.GetNeighbors(i).Count();
        return total / 2; // each undirected edge is stored from both ends
    }

    private static int BfsCount(Graph graph, int startNode)
    {
        var visited = new HashSet<int> { startNode };
        var queue = new Queue<int>();
        queue.Enqueue(startNode);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in graph.GetNeighbors(current))
                if (visited.Add(neighbor))
                    queue.Enqueue(neighbor);
        }

        return visited.Count;
    }

    private static HashSet<(int, int)> EdgeSet(IDataset? dataset)
    {
        var graph = Assert.IsType<Graph>(dataset);
        var set = new HashSet<(int, int)>();
        for (int i = 0; i < graph.NodeCount; i++)
            foreach (var neighbor in graph.GetNeighbors(i))
                set.Add((Math.Min(i, neighbor), Math.Max(i, neighbor))); // canonical, order-independent
        return set;
    }
}