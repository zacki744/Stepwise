using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record CellFloodedPayload(int Row, int Column, int OldValue, int NewValue);

/// <summary>
/// Classic flood fill (paint bucket): starting from (row, column), replaces
/// every 4-connected cell sharing the start cell's original value with
/// newValue. Iterative (stack-based), not recursive, to avoid stack-depth
/// limits on large grids. No-op if newValue already equals the start cell's
/// value — nothing would visibly change.
/// </summary>
public sealed class FloodFillOperation(int row, int column, int newValue) : IOperation<Matrix>
{
    public OperationMetadata Metadata { get; } = new("flood-fill", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        var working = new Matrix(input.Rows, input.Columns);
        for (int r = 0; r < input.Rows; r++)
            for (int c = 0; c < input.Columns; c++)
                working[r, c] = input[r, c];

        var trace = new TraceRecorder(context);
        int targetValue = working[row, column];
        if (targetValue == newValue)
            return new OperationExecutionResult(true, working, null, trace, null);

        var stack = new Stack<(int Row, int Col)>();
        stack.Push((row, column));

        while (stack.Count > 0)
        {
            var (r, c) = stack.Pop();
            if (r < 0 || r >= working.Rows || c < 0 || c >= working.Columns) continue;
            if (working[r, c] != targetValue) continue;

            working[r, c] = newValue;
            trace.Add(new DomainStepEvent("CellFlooded", $"({r},{c}): {targetValue} -> {newValue}",
                new CellFloodedPayload(r, c, targetValue, newValue)));

            stack.Push((r - 1, c));
            stack.Push((r + 1, c));
            stack.Push((r, c - 1));
            stack.Push((r, c + 1));
        }

        return new OperationExecutionResult(true, working, null, trace, null);
    }
}