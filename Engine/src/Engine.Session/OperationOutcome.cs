using Engine.Domain;
using Engine.Domain.Tracing;

namespace Engine.Session;

public sealed class OperationOutcome(
    bool Succeeded,
    IReadOnlyList<DomainStepEvent> Trace,
    object? Value,
    string? FailureReason,
    IDataset? CommittedState = null)
{
    public bool Succeeded { get; } = Succeeded;
    public IReadOnlyList<DomainStepEvent> Trace { get; } = Trace;
    public object? Value { get; } = Value;
    public string? FailureReason { get; } = FailureReason;

    /// <summary>
    /// The dataset this operation committed as the session's new state, or null when it
    /// committed none (queries, failures). Carried here so the gateway never has to re-read
    /// session state after the lock is released.
    /// </summary>
    public IDataset? CommittedState { get; } = CommittedState;
}