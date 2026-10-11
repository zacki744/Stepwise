using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;
public enum StateEffect
{
    ReadOnly,
    ReplacesState,
    ProducesNewDataset,
    ProducesValue,
    ReplacesStateAndProducesValue
}

public sealed record OperationMetadata(string OperationId, StateEffect StateEffect);

public interface IOperation<in TInput> where TInput : IDataset
{
    OperationMetadata Metadata { get; }
    OperationExecutionResult Execute(TInput input, OperationContext context);
}