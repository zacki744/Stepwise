using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record TreeClearedPayload(int NodesRemoved);

/// <summary>Destruction. Value is the number of LIVE nodes removed. The new tree has zero slots, so this also resets SlotCount.</summary>
public sealed class ClearTreeOperation : IOperation<Tree>
{
    public OperationMetadata Metadata { get; } = new("tree-clear", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Tree input, OperationContext context)
    {
        var trace = new TraceRecorder(context);
        int removed = input.InOrder().Count();
        context.Checkpoint(removed);

        trace.Add(new DomainStepEvent("TreeCleared", $"Removed {removed} node(s)", new TreeClearedPayload(removed)));
        return new OperationExecutionResult(true, Tree.Empty(), removed, trace, null);
    }
}