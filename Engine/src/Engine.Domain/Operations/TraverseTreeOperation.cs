using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public enum TraversalOrder { Pre, In, Post, Level }

public sealed record TreeNodeVisitedPayload(int NodeIndex, int Value, int Position);

/// <summary>All four traversals are iterative: a 200,000-deep chain must not overflow the stack.</summary>
public sealed class TraverseTreeOperation : IOperation<Tree>
{
    private readonly TraversalOrder _order;

    public TraverseTreeOperation(string order)
    {
        ArgumentNullException.ThrowIfNull(order);
        _order = ParseOrder(order);
    }

    public OperationMetadata Metadata { get; } = new("tree-traverse", StateEffect.ProducesValue);

    public static TraversalOrder ParseOrder(string order) => order.ToLowerInvariant() switch
    {
        "pre" => TraversalOrder.Pre,
        "in" => TraversalOrder.In,
        "post" => TraversalOrder.Post,
        "level" => TraversalOrder.Level,
        _ => throw new ArgumentException($"Unknown traversal order '{order}'; expected pre, in, post or level")
    };

    public OperationExecutionResult Execute(Tree input, OperationContext context)
    {
        var trace = new TraceRecorder(context);
        var visited = new List<int>();

        void Visit(int slot)
        {
            int value = input.GetValue(slot);
            visited.Add(value);
            trace.Add(new DomainStepEvent("TreeNodeVisited", $"Visited {value} (#{visited.Count})",
                new TreeNodeVisitedPayload(slot, value, visited.Count)));
        }

        if (input.RootIndex is int root)
        {
            switch (_order)
            {
                case TraversalOrder.Pre: PreOrder(input, root, Visit); break;
                case TraversalOrder.In: foreach (var slot in input.InOrder()) Visit(slot); break;
                case TraversalOrder.Post: PostOrder(input, root, Visit); break;
                case TraversalOrder.Level: LevelOrder(input, root, Visit); break;
            }
        }

        return new OperationExecutionResult(true, null, visited, trace, null);
    }

    private static void PreOrder(Tree tree, int root, Action<int> visit)
    {
        var stack = new Stack<int>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            int node = stack.Pop();
            visit(node);
            if (tree.RightChild(node) is int right) stack.Push(right); // right first, so left pops first
            if (tree.LeftChild(node) is int left) stack.Push(left);
        }
    }

    private static void PostOrder(Tree tree, int root, Action<int> visit)
    {
        var stack = new Stack<int>();
        int? current = root;
        int lastVisited = -1;
        while (current is not null || stack.Count > 0)
        {
            while (current is int node)
            {
                stack.Push(node);
                current = tree.LeftChild(node);
            }

            int top = stack.Peek();
            if (tree.RightChild(top) is int right && right != lastVisited)
            {
                current = right; // descend into the right subtree before visiting this node
            }
            else
            {
                visit(top);
                lastVisited = top;
                stack.Pop();
            }
        }
    }

    private static void LevelOrder(Tree tree, int root, Action<int> visit)
    {
        var queue = new Queue<int>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            visit(node);
            if (tree.LeftChild(node) is int left) queue.Enqueue(left);
            if (tree.RightChild(node) is int right) queue.Enqueue(right);
        }
    }
}