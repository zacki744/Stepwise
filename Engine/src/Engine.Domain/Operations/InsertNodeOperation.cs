using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record NodeComparedPayload(int NodeIndex, int NodeValue);
public sealed record NodeInsertedPayload(int Value, int Index, int? ParentIndex);

public sealed class InsertNodeOperation(int value) : IOperation<Tree>
{
    public OperationMetadata Metadata { get; } = new("insert-node", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Tree input, OperationContext context)
    {
		DatasetLimits.EnsureWithinLimit((long)input.SlotCount + 1, "Tree");
		var values = input.CloneValues();
        var left = input.CloneLeft();
        var right = input.CloneRight();
        var trace = new TraceRecorder(context);

        if (input.RootIndex is null)
        {
            values.Add(value);
            left.Add(-1);
            right.Add(-1);
            trace.Add(new DomainStepEvent("NodeInserted", $"Inserted {value} as root",
                new NodeInsertedPayload(value, 0, null)));
            return Result(values, left, right, root: 0, trace);
        }

        int current = input.RootIndex.Value;
        int parent = -1;
        bool goLeft = false;

        while (current != -1)
        {
            int currentValue = values[current]!.Value;
            trace.Add(new DomainStepEvent("NodeCompared", $"Compared {value} with {currentValue} at {current}",
                new NodeComparedPayload(current, currentValue)));

            parent = current;
            if (value < currentValue) { goLeft = true; current = left[current]; }
            else { goLeft = false; current = right[current]; }
        }

        int newIndex = values.Count;
        values.Add(value);
        left.Add(-1);
        right.Add(-1);
        if (goLeft) left[parent] = newIndex; else right[parent] = newIndex;

        trace.Add(new DomainStepEvent("NodeInserted", $"Inserted {value} at {newIndex} under {parent}",
            new NodeInsertedPayload(value, newIndex, parent)));

        return Result(values, left, right, input.RootIndex.Value, trace);
    }

    private static OperationExecutionResult Result(
        List<int?> values, List<int> left, List<int> right, int root, TraceRecorder trace) =>
        new(Succeeded: true, NewState: Tree.FromArrays(values, left, right, root),
            Value: null, Trace: trace, FailureReason: null);
}