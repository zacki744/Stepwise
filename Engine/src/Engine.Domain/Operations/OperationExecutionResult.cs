namespace Engine.Domain.Operations
{
    using Engine.Domain;
    using Engine.Domain.Tracing;

    public sealed record OperationExecutionResult(
        bool Succeeded,
        IDataset? NewState,        // set only when StateEffect is ReplacesState/ProducesNewDataset and Succeeded
        object? Value,             // set only when StateEffect is ProducesValue and Succeeded
        IReadOnlyList<DomainStepEvent> Trace,
        string? FailureReason);
}