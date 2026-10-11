using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record NodeMappedPayload(int Row, int Column, int NodeIndex);
public sealed record MatrixEdgeAddedPayload(int From, int To);

/// <summary>
/// Converts a Matrix into a Graph: cell value 0 means "open/passable" and
/// becomes a node; any non-zero value is a wall and is excluded entirely.
/// Adjacent open cells (4-connected) get an edge. Node indices are assigned
/// in row-major scan order among open cells only — Value reports that
/// mapping explicitly (positionsByNodeIndex[i] = the (row, column) that
/// became node i), since a client needs it to translate a later operation's
/// node-index results (e.g. shortest-path) back to grid coordinates.
/// A genuine Convert: NewState is a different dataset TYPE than the input,
/// not just a new state of the same type — committing this to a session
/// changes that session's dataset type from Matrix to Graph.
/// </summary>
public sealed class MatrixToGraphOperation : IOperation<Matrix>
{
    public OperationMetadata Metadata { get; } = new("matrix-to-graph", StateEffect.ReplacesStateAndProducesValue);
	public sealed record GridPosition(int Row, int Column);

	public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        var trace = new TraceRecorder(context);
        var nodeIndexByCell = new Dictionary<(int Row, int Col), int>();
		var positionsByNodeIndex = new List<GridPosition>();

		for (int r = 0; r < input.Rows; r++)
        {
            for (int c = 0; c < input.Columns; c++)
            {
                if (input[r, c] != 0) continue; // wall — excluded entirely, not even an isolated node

                int index = positionsByNodeIndex.Count;
                nodeIndexByCell[(r, c)] = index;
				positionsByNodeIndex.Add(new GridPosition(r, c));

				trace.Add(new DomainStepEvent("NodeMapped", $"({r},{c}) -> node {index}",
                    new NodeMappedPayload(r, c, index)));
            }
        }

        var edges = new List<(int From, int To, double Weight)>();
        foreach (var ((row, col), index) in nodeIndexByCell)
        {
            // Only check right/down neighbors — each undirected edge gets
            // visited from exactly one of its two endpoints this way,
            // avoiding duplicate edges without needing a seen-set.
            TryAddEdge(row, col, row, col + 1);
            TryAddEdge(row, col, row + 1, col);

            void TryAddEdge(int r1, int c1, int r2, int c2)
            {
                if (!nodeIndexByCell.TryGetValue((r2, c2), out var neighborIndex)) return;
                edges.Add((index, neighborIndex, 1.0));
                edges.Add((neighborIndex, index, 1.0));
                trace.Add(new DomainStepEvent("EdgeAdded", $"node {index} <-> node {neighborIndex}",
                    new MatrixEdgeAddedPayload(index, neighborIndex)));
            }
        }

        var graph = new Graph(positionsByNodeIndex.Count, edges);

        return new OperationExecutionResult(true, graph, positionsByNodeIndex, trace, null);
    }
}