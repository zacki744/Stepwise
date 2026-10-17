using Engine.Domain;

namespace Engine.Session;

public sealed class Session
{
    public Guid Id { get; }
    public IDataset Dataset { get; private set; }
    public DateTime LastActiveUtc { get; private set; }

    /// <summary>What this session is charged against the server-wide budget. Written only under the manager's budget lock.</summary>
    internal long Bytes;

    internal object SyncRoot { get; } = new();

    public Session(IDataset initialDataset)
    {
        Id = Guid.NewGuid();
        Dataset = initialDataset;
        Bytes = initialDataset.EstimatedBytes;
        LastActiveUtc = DateTime.UtcNow;
    }

    public void Touch() => LastActiveUtc = DateTime.UtcNow;

    internal void CommitState(IDataset newDataset)
    {
        Dataset = newDataset;
        Touch();
    }
}