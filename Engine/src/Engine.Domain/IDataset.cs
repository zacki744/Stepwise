namespace Engine.Domain;

/// <summary>
/// Marker interface for any committed dataset a session can hold.
/// Deliberately empty — capability interfaces (ITraversable, etc.)
/// describe what operations can do; this just identifies "a dataset."
/// </summary>
public interface IDataset
{
    /// <summary>
    /// Rough in-memory size of this dataset in bytes (its arrays, not object overhead). Sessions are
    /// charged this against a server-wide budget, so it only needs to be proportional and cheap, not exact.
    /// </summary>
    long EstimatedBytes { get; }
}