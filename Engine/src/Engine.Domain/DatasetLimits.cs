namespace Engine.Domain;

/// <summary>
/// Hard ceiling on dataset size. A constant rather than configuration on
/// purpose: it's a domain invariant enforced inside constructors, and
/// threading a setting through every one of them isn't worth it yet.
/// 500,000 matches the 1000x500 scale the design has used since day one.
/// </summary>
public static class DatasetLimits
{
    public const int MaxCells = 500_000;
    /// <summary>
    /// Directed edges per graph. A fully open 4-connected grid at the cell limit (the largest thing
    /// matrix-to-graph can emit) needs ~2.0M, so this leaves headroom above it.
    /// </summary>
    public const int MaxEdges = 2_500_000;
    /// <summary>Most list items any single operation result may carry. Keeps replies small regardless of dataset size.</summary>
    public const int MaxReturnedItems = 50_000;
    /// <summary>Takes a long so callers can multiply in 64-bit and avoid int wraparound.</summary>
    public static void EnsureWithinLimit(long cells, string what)
    {
        if (cells > MaxCells)
            throw new ArgumentException(
                $"{what} would contain {cells:N0} cells, which exceeds the limit of {MaxCells:N0}");
    }
}