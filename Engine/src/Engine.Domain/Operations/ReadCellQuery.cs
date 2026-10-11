using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed class ReadCellQuery(int row, int column) : IOperation<Matrix>
{
    public OperationMetadata Metadata { get; } = new("read-cell", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Matrix input, OperationContext context) =>
        new(Succeeded: true, NewState: null, Value: input[row, column], Trace: [], FailureReason: null);
}