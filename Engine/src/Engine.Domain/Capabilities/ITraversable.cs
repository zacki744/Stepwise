namespace Engine.Domain.Capabilities;

/// <summary>
/// A dataset whose elements have neighbors that can be enumerated.
/// Contract: GetNeighbors never mutates the dataset, and the returned
/// sequence contains no duplicates for a given node.
/// </summary>
public interface ITraversable<TNode>
{
    IEnumerable<TNode> GetNeighbors(TNode node);
}