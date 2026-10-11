using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record AdjacencyCellPayload(int Row, int Column, int Count);

/// <summary>
/// Convert: Graph -> Matrix. Cell [i, j] is the number of directed edges from node i to node j (so 0 or
/// 1 for a simple graph, and symmetric for an undirected one). Weights are NOT stored: the matrix holds
/// integers and weights are doubles, so any conversion would be lossy. Row i is node i, so
/// `aggregate` on a row gives that node's out-degree. Needs 1..707 nodes (the 500,000-cell limit).
/// </summary>
public sealed class GraphToMatrixOperation : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("graph-to-matrix", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        if (input.NodeCount == 0)
            throw new ArgumentException("An empty graph has no adjacency matrix (a matrix needs at least one row and column)");

        var matrix = new Matrix(input.NodeCount, input.NodeCount); // enforces the cell limit
        context.Checkpoint(input.EdgeCount);

        var trace = new TraceRecorder(context);
        foreach (var (from, to, _) in input.AllDirectedEdges())
        {
            matrix[from, to]++;
            trace.Add(new DomainStepEvent("AdjacencyCellSet", $"[{from},{to}] = {matrix[from, to]}",
                new AdjacencyCellPayload(from, to, matrix[from, to])));
        }

        return new OperationExecutionResult(true, matrix, null, trace, null);
    }
}