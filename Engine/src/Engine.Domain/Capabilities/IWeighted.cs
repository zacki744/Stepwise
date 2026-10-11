namespace Engine.Domain.Capabilities;

/// <summary>
/// A dataset where relationships between nodes carry a numeric weight.
/// Contract: GetWeight throws ArgumentException if no edge exists between
/// the given nodes — check via GetNeighbors/GetWeightedNeighbors first if
/// existence isn't already known. Weights may be negative or zero; this
/// interface makes no claim about sign or range.
/// </summary>
public interface IWeighted<TNode>
{
    double GetWeight(TNode from, TNode to);
}