namespace Engine.Domain.Operations;

public static class OperationExtensions
{
    /// <summary>Convenience for tests and callers that don't need limits.</summary>
    public static OperationExecutionResult Execute<TInput>(this IOperation<TInput> operation, TInput input)
        where TInput : IDataset
        => operation.Execute(input, OperationContext.Unbounded);
}