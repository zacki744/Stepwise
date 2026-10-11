using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

/// <summary>
/// Fills every cell: even (row+column) gets 2, odd gets 1.
/// The canonical "prove the loop works" example from earlier design discussion.
/// </summary>
public sealed class FillMatrixOperation : IOperation<Matrix>
{
    public sealed record CellFilledPayload(int Row, int Column, int Value);
    public OperationMetadata Metadata { get; } =
        new("fill-even-odd", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Matrix input, OperationContext context)
    {
        // Internal working copy — per the transactional model, we mutate
        // this freely and only expose it if execution succeeds.
        var working = new Matrix(input.Rows, input.Columns);
        var trace = new TraceRecorder(context);

        for (int r = 0; r < input.Rows; r++)
        {
            for (int c = 0; c < input.Columns; c++)
            {
                int value = (r + c) % 2 == 0 ? 2 : 1;
                working[r, c] = value;
                trace.Add(new DomainStepEvent("CellFilled", $"({r},{c}) = {value}", new CellFilledPayload(r, c, value)));
            }
        }

        return new OperationExecutionResult(
            Succeeded: true,
            NewState: working,
            Value: null,
            Trace: trace,
            FailureReason: null);
    }
}