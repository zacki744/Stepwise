using Engine.Domain;

namespace Engine.Session;

public sealed class Session
{
    public Guid Id { get; }
    public IDataset Dataset { get; private set; }
    public DateTime LastActiveUtc { get; private set; }

    internal object SyncRoot { get; } = new();

    public Session(IDataset initialDataset)
    {
        Id = Guid.NewGuid();
        Dataset = initialDataset;
        LastActiveUtc = DateTime.UtcNow;
    }

    public void Touch() => LastActiveUtc = DateTime.UtcNow;

    internal void CommitState(IDataset newDataset)
    {
        Dataset = newDataset;
        Touch();
    }
}