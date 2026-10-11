using System.Text.Json;
using Engine.Domain;
using Engine.Domain.Operations;

namespace Engine.Session;

public interface IOperationHandler
{
    string OperationId { get; }
    string DatasetType { get; }
    OperationOutcome Execute(
        SessionManager manager, Guid sessionId,
        IReadOnlyDictionary<string, JsonElement> options, OperationContext context);
}

public sealed class OperationHandler<TDataset>(
    string operationId,
    Func<IReadOnlyDictionary<string, JsonElement>, IOperation<TDataset>> factory) : IOperationHandler
    where TDataset : class, IDataset
{
    public string OperationId => operationId;

    // Relies on dataset type names matching DatasetFactory's wire discriminators, lowercased
    // (Matrix -> "matrix"). OperationCatalogTests exists to catch drift.
    public string DatasetType => typeof(TDataset).Name.ToLowerInvariant();

    public OperationOutcome Execute(
        SessionManager manager, Guid sessionId,
        IReadOnlyDictionary<string, JsonElement> options, OperationContext context)
        => manager.Execute(sessionId, factory(options), context);
}

public sealed class OperationRegistry
{
    private readonly Dictionary<string, IOperationHandler> _handlers = new();

    public void Register(IOperationHandler handler) => _handlers[handler.OperationId] = handler;

    public IOperationHandler? TryGet(string operationId) => _handlers.GetValueOrDefault(operationId);

    public IEnumerable<IOperationHandler> All => _handlers.Values;
}