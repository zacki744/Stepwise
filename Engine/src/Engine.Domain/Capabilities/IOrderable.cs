namespace Engine.Domain.Capabilities;

/// <summary>
/// A dataset whose elements have a well-defined total order, exposed as an
/// ascending traversal. Contract: InOrder() never mutates the dataset, and
/// yields every element exactly once, in ascending order by whatever
/// ordering key the concrete type defines (a BST's stored values, here).
/// </summary>
public interface IOrderable<TNode>
{
    IEnumerable<TNode> InOrder();
}