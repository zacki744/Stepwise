namespace Engine.Domain;

/// <summary>
/// Marker interface for any committed dataset a session can hold.
/// Deliberately empty — capability interfaces (ITraversable, etc.)
/// describe what operations can do; this just identifies "a dataset."
/// </summary>
public interface IDataset;