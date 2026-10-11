using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engine.Protocol;

// Client → server
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CreateSessionMessage), "createSession")]
[JsonDerivedType(typeof(ExecuteOperationMessage), "executeOperation")]
[JsonDerivedType(typeof(RehydrateSessionMessage), "rehydrateSession")]
public abstract record ClientMessage;

public sealed record CreateSessionMessage(string DatasetType, JsonElement InitialPayload) : ClientMessage;

public sealed record ExecuteOperationMessage(
    Guid RequestId,          // dedup/idempotency, per the earlier review
    string OperationId,
    Dictionary<string, JsonElement> Options) : ClientMessage;

// Server → client
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SessionReadyMessage), "sessionReady")]
[JsonDerivedType(typeof(StepEventMessage), "stepEvent")]
[JsonDerivedType(typeof(OperationCompleteMessage), "operationComplete")]
[JsonDerivedType(typeof(OperationFailedMessage), "operationFailed")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
public abstract record ServerMessage;

public sealed record SessionReadyMessage(Guid SessionId) : ServerMessage;

public sealed record StepEventMessage(string StepType, string Description, JsonElement? Payload) : ServerMessage;
public sealed record DatasetSnapshotDto(string DatasetType, JsonElement Payload);

public sealed record OperationCompleteMessage(
    Guid RequestId,
    JsonElement? ResultSummary,
    DatasetSnapshotDto? FinalState = null) : ServerMessage;
public sealed record OperationFailedMessage(Guid RequestId, string Reason) : ServerMessage;

public sealed record ErrorMessage(string Code, string Message) : ServerMessage;

public sealed record LoggedOperationDto(string OperationId, Dictionary<string, JsonElement> Options);

public sealed record RehydrateSessionMessage(
    string DatasetType,
    JsonElement InitialPayload,
    IReadOnlyList<LoggedOperationDto> OpLog) : ClientMessage;