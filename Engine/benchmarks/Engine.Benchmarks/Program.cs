using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Engine.Domain;
using Engine.Domain.Datasets;
using Engine.Domain.Operations;

BenchmarkSwitcher.FromAssembly(typeof(WorstCaseOperations).Assembly).Run(args);

/// <summary>
/// Measures time and allocated memory of operations on datasets at the size limits. The "Allocated" column is
/// the transient memory one running operation needs on top of the dataset itself; multiply it by the number of
/// concurrent operations (ExecutionPolicy.MaxConcurrentOperations) when sizing the container memory limit.
/// </summary>
[MemoryDiagnoser]
public class WorstCaseOperations
{
    private Graph _sparseMax = null!;   // the edge limit
    private Graph _denseNodes = null!;  // many nodes, few edges
    private Matrix _openGrid = null!;   // the cell limit, all open

    [GlobalSetup]
    public void Setup()
    {
        _sparseMax = new GenerateRandomGraphOperation(nodes: 100_000, connections: DatasetLimits.MaxEdges / 2, seed: 1)
            .Execute(Graph.Empty(1)).NewState as Graph ?? throw new InvalidOperationException("generator returned no graph");
        _denseNodes = new GenerateRandomGraphOperation(nodes: 400_000, connections: 600_000, seed: 2)
            .Execute(Graph.Empty(1)).NewState as Graph ?? throw new InvalidOperationException("generator returned no graph");
        _openGrid = new Matrix(1000, 500);
    }

    [Benchmark] public object? ShortestPath_MaxEdges() => new ShortestPathOperation(0, 99_999).Execute(_sparseMax);
    [Benchmark] public object? Components_MaxEdges() => new FindConnectedComponentsOperation().Execute(_sparseMax);
    [Benchmark] public object? Mst_MaxEdges() => new MinimumSpanningTreeOperation().Execute(_sparseMax);
    [Benchmark] public object? Components_ManyNodes() => new FindConnectedComponentsOperation().Execute(_denseNodes);
    [Benchmark] public object? MatrixToGraph_OpenGrid() => new MatrixToGraphOperation().Execute(_openGrid);
}
