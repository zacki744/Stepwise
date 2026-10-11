using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record NodeRemovedPayload(int Value);
public sealed record SuccessorFoundPayload(int NodeIndex, int SuccessorIndex);

/// <summary>
/// Standard BST deletion: 0, 1, and 2-child cases. For 2 children, copies
/// the in-order successor's value in and recursively removes the successor
/// instead (which — by construction, being leftmost in the right subtree —
/// always has at most one child, so recursion bottoms out in one more step).
/// </summary>
public sealed class RemoveNodeOperation(int value) : IOperation<Tree>
{
    public OperationMetadata Metadata { get; } = new("remove-node", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Tree input, OperationContext context)
    {
        var values = input.CloneValues();
        var left = input.CloneLeft();
        var right = input.CloneRight();
        var trace = new TraceRecorder(context);

        int root = input.RootIndex ?? -1;
        int current = root;
        int parent = -1;
        bool isLeftChild = false;

        while (current != -1)
        {
            int currentValue = values[current]!.Value;
            trace.Add(new DomainStepEvent("NodeCompared", $"Compared {value} with {currentValue} at {current}",
                new NodeComparedPayload(current, currentValue)));

            if (value == currentValue) break;
            parent = current;
            if (value < currentValue) { isLeftChild = true; current = left[current]; }
            else { isLeftChild = false; current = right[current]; }
        }

        if (current == -1)
        {
            trace.Add(new DomainStepEvent("NodeNotFound", $"{value} not present in tree"));
            return new OperationExecutionResult(true, Tree.FromArrays(values, left, right, root), false, trace, null);
        }

        int newRoot = RemoveAt(current, parent, isLeftChild, values, left, right, root, trace);
        trace.Add(new DomainStepEvent("NodeRemoved", $"Removed {value}", new NodeRemovedPayload(value)));

        return new OperationExecutionResult(true, Tree.FromArrays(values, left, right, newRoot), true, trace, null);
    }

    private static int RemoveAt(int nodeIndex, int parentIndex, bool isLeftChild,
        List<int?> values, List<int> left, List<int> right, int root, TraceRecorder trace)
    {
        if (left[nodeIndex] != -1 && right[nodeIndex] != -1)
        {
            // Two children: find in-order successor (leftmost of right subtree).
            int successorParent = nodeIndex;
            int successor = right[nodeIndex];
            while (left[successor] != -1)
            {
                successorParent = successor;
                successor = left[successor];
            }

            trace.Add(new DomainStepEvent("SuccessorFound", $"Successor of {nodeIndex} is {successor}",
                new SuccessorFoundPayload(nodeIndex, successor)));

            values[nodeIndex] = values[successor];

            bool successorIsLeftChild = successorParent != nodeIndex && left[successorParent] == successor;
            // Successor has at most one (right) child by construction — this
            // recursive call always lands in the 0/1-child branch below, and
            // never touches root (its parent chain is never -1), so the
            // returned value is always the unchanged root — safe to discard.
            RemoveAt(successor, successorParent, successorIsLeftChild, values, left, right, root, trace);
            return root;
        }

        // 0 or 1 child: splice the single child (or nothing) into the parent's slot.
        int child = left[nodeIndex] != -1 ? left[nodeIndex] : right[nodeIndex];

        if (parentIndex == -1)
            return child; // removing the root — child (or -1) becomes the new root

        if (isLeftChild) left[parentIndex] = child; else right[parentIndex] = child;
        return root;
    }
}