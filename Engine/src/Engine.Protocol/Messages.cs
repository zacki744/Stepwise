using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engine.Protocol;

// Client → server
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CreateSessionMessage), "createSession")]
[JsonDerivedType(typeof(ExecuteOperationMessage), "executeOperation")]
[JsonDerivedType(typeof(RehydrateSessionMessage), "rehydrateSession")]
[JsonDerivedType(typeof(UploadChunkMessage), "uploadChunk")]
[JsonDerivedType(typeof(GetStateMessage), "getState")]
public abstract record ClientMessage;

public sealed record CreateSessionMessage(string DatasetType, JsonElement InitialPayload) : ClientMessage;

public sealed record ExecuteOperationMessage(
    Guid RequestId,          // dedup/idempotency, per the earlier review
    string OperationId,
    Dictionary<string, JsonElement> Options) : ClientMessage;

/// <summary>
/// One piece of a createSession or rehydrateSession message too large for a single frame. The client
/// serialises the whole message to JSON text, cuts it into pieces (never splitting a surrogate pair),
/// and sends them in order with Index 0..Total-1 and the same UploadId. When the last piece arrives the
/// server handles the reassembled message exactly as if it had arrived in one frame.
/// </summary>
public sealed record UploadChunkMessage(Guid UploadId, int Index, int Total, string Data) : ClientMessage;

/// <summary>Asks for the session's current dataset. Answered with a state message (or operationFailed).</summary>
public sealed record GetStateMessage(Guid RequestId) : ClientMessage;

// Server → client
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SessionReadyMessage), "sessionReady")]
[JsonDerivedType(typeof(StepEventMessage), "stepEvent")]
[JsonDerivedType(typeof(OperationCompleteMessage), "operationComplete")]
[JsonDerivedType(typeof(OperationFailedMessage), "operationFailed")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
[JsonDerivedType(typeof(StateMessage), "state")]
public abstract record ServerMessage;

public sealed record SessionReadyMessage(Guid SessionId) : ServerMessage;

public sealed record StepEventMessage(string StepType, string Description, JsonElement? Payload) : ServerMessage;
public sealed record DatasetSnapshotDto(string DatasetType, JsonElement Payload);

/// <summary>
/// What running an operation cost. Steps is how many steps the operation traced (even if the trace was
/// coalesced), WorkUnits the cooperative work it was charged against its budget, ElapsedMs the wall time
/// spent computing (including any wait for the session lock). Informational only: never replayed.
/// </summary>
public sealed record ExecutionStatsDto(int Steps, long WorkUnits, double ElapsedMs);

public sealed record OperationCompleteMessage(
    Guid RequestId,
    JsonElement? ResultSummary,
    DatasetSnapshotDto? FinalState = null,
    ExecutionStatsDto? Stats = null) : ServerMessage;
public sealed record OperationFailedMessage(Guid RequestId, string Reason) : ServerMessage;

public sealed record StateMessage(Guid RequestId, DatasetSnapshotDto State) : ServerMessage;

public sealed record ErrorMessage(string Code, string Message) : ServerMessage;

public sealed record LoggedOperationDto(string OperationId, Dictionary<string, JsonElement> Options);

public sealed record RehydrateSessionMessage(
    string DatasetType,
    JsonElement InitialPayload,
    IReadOnlyList<LoggedOperationDto> OpLog) : ClientMessage;