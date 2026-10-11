namespace Engine.Session;

public sealed class DatasetMismatchException(string sessionDataset, string expectedDataset)
    : InvalidOperationException($"This session holds a {sessionDataset}, but the operation works on a {expectedDataset}")
{
    public string SessionDataset { get; } = sessionDataset;
    public string ExpectedDataset { get; } = expectedDataset;
}