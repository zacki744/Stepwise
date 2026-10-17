using System.Text.Json;
using System.Text.Json.Nodes;
using Engine.Protocol;
using Xunit;

namespace Engine.Protocol.Tests;

/// <summary>
/// Pins the exact JSON of every protocol message. The frontend (and any other client) is written against
/// this text, so a change here is a breaking change and must be made on purpose: a failing test means "you
/// just changed the wire format", not "update the expectation". Property order does not matter to clients and
/// is not compared; names, values, nulls and nesting are.
/// </summary>
public class WireFormatGoldenTests
{
    private static readonly Guid Request = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Other = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static void AssertWire(string expected, ServerMessage message)
    {
        var actual = JsonSerializer.Serialize(message, ProtocolJson.Options);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)),
            $"Wire format changed.\nExpected: {expected}\nActual:   {actual}");
    }

    // ---------- server -> client ----------

    [Fact]
    public void SessionReady() => AssertWire(
        """{"type":"sessionReady","sessionId":"11111111-2222-3333-4444-555555555555"}""",
        new SessionReadyMessage(Request));

    [Fact]
    public void StepEvent() => AssertWire(
        """{"type":"stepEvent","stepType":"visit","description":"Visited node 3","payload":{"node":3}}""",
        new StepEventMessage("visit", "Visited node 3", Json("""{"node":3}""")));

    [Fact]
    public void StepEvent_WithoutPayload_SendsNull() => AssertWire(
        """{"type":"stepEvent","stepType":"visit","description":"x","payload":null}""",
        new StepEventMessage("visit", "x", null));

    [Fact]
    public void OperationComplete_Full() => AssertWire(
        """
        {"type":"operationComplete","requestId":"11111111-2222-3333-4444-555555555555",
         "resultSummary":{"sum":10},
         "finalState":{"datasetType":"matrix","payload":{"rows":1,"columns":1,"cells":[7]}},
         "stats":{"steps":4,"workUnits":120,"elapsedMs":1.5}}
        """,
        new OperationCompleteMessage(Request, Json("""{"sum":10}"""),
            new DatasetSnapshotDto("matrix", Json("""{"rows":1,"columns":1,"cells":[7]}""")),
            new ExecutionStatsDto(4, 120, 1.5)));

    [Fact]
    public void OperationComplete_Minimal_SendsExplicitNulls() => AssertWire(
        """{"type":"operationComplete","requestId":"11111111-2222-3333-4444-555555555555","resultSummary":null,"finalState":null,"stats":null}""",
        new OperationCompleteMessage(Request, null));

    [Fact]
    public void OperationFailed() => AssertWire(
        """{"type":"operationFailed","requestId":"11111111-2222-3333-4444-555555555555","reason":"Invalid options: nope"}""",
        new OperationFailedMessage(Request, "Invalid options: nope"));

    [Fact]
    public void State() => AssertWire(
        """{"type":"state","requestId":"11111111-2222-3333-4444-555555555555","state":{"datasetType":"tree","payload":{"root":-1,"slotCount":0,"nodes":[]}}}""",
        new StateMessage(Request, new DatasetSnapshotDto("tree", Json("""{"root":-1,"slotCount":0,"nodes":[]}"""))));

    [Fact]
    public void Error() => AssertWire(
        """{"type":"error","code":"server-busy","message":"try again"}""",
        new ErrorMessage("server-busy", "try again"));

    // ---------- client -> server ----------

    private static T ParseClient<T>(string json) where T : ClientMessage =>
        Assert.IsType<T>(JsonSerializer.Deserialize<ClientMessage>(json, ProtocolJson.Options));

    [Fact]
    public void CreateSession_Parses()
    {
        var message = ParseClient<CreateSessionMessage>(
            """{"type":"createSession","datasetType":"matrix","initialPayload":{"rows":2,"columns":3}}""");

        Assert.Equal("matrix", message.DatasetType);
        Assert.Equal(2, message.InitialPayload.GetProperty("rows").GetInt32());
        Assert.Equal(3, message.InitialPayload.GetProperty("columns").GetInt32());
    }

    [Fact]
    public void ExecuteOperation_Parses()
    {
        var message = ParseClient<ExecuteOperationMessage>(
            """{"type":"executeOperation","requestId":"11111111-2222-3333-4444-555555555555","operationId":"rotate","options":{"quarterTurns":2}}""");

        Assert.Equal(Request, message.RequestId);
        Assert.Equal("rotate", message.OperationId);
        Assert.Equal(2, message.Options["quarterTurns"].GetInt32());
    }

    [Fact]
    public void RehydrateSession_Parses()
    {
        var message = ParseClient<RehydrateSessionMessage>(
            """
            {"type":"rehydrateSession","datasetType":"matrix","initialPayload":{"rows":1,"columns":1},
             "opLog":[{"operationId":"set-cell","options":{"row":0,"column":0,"value":4}},{"operationId":"transpose","options":{}}]}
            """);

        Assert.Equal("matrix", message.DatasetType);
        Assert.Equal(new[] { "set-cell", "transpose" }, message.OpLog.Select(o => o.OperationId));
        Assert.Equal(4, message.OpLog[0].Options["value"].GetInt32());
    }

    [Fact]
    public void UploadChunk_Parses()
    {
        var message = ParseClient<UploadChunkMessage>(
            """{"type":"uploadChunk","uploadId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","index":1,"total":3,"data":"abc"}""");

        Assert.Equal(Other, message.UploadId);
        Assert.Equal(1, message.Index);
        Assert.Equal(3, message.Total);
        Assert.Equal("abc", message.Data);
    }

    [Fact]
    public void GetState_Parses()
    {
        var message = ParseClient<GetStateMessage>(
            """{"type":"getState","requestId":"11111111-2222-3333-4444-555555555555"}""");

        Assert.Equal(Request, message.RequestId);
    }

    [Fact]
    public void Parsing_IgnoresCaseOfPropertyNames()
    {
        var message = ParseClient<GetStateMessage>(
            """{"type":"getState","RequestId":"11111111-2222-3333-4444-555555555555"}""");

        Assert.Equal(Request, message.RequestId);
    }

    [Fact]
    public void AnUnknownMessageType_IsRefused() =>
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<ClientMessage>("""{"type":"dropTables"}""", ProtocolJson.Options));

    // ---------- every message type has a golden ----------

    [Fact]
    public void EveryDerivedMessageType_IsCoveredAbove()
    {
        var covered = new HashSet<Type>
        {
            typeof(SessionReadyMessage), typeof(StepEventMessage), typeof(OperationCompleteMessage),
            typeof(OperationFailedMessage), typeof(StateMessage), typeof(ErrorMessage),
            typeof(CreateSessionMessage), typeof(ExecuteOperationMessage), typeof(RehydrateSessionMessage),
            typeof(UploadChunkMessage), typeof(GetStateMessage)
        };

        var declared = typeof(ClientMessage).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && (t.IsSubclassOf(typeof(ClientMessage)) || t.IsSubclassOf(typeof(ServerMessage))));

        // A new message type fails here until someone adds its golden test and lists it.
        Assert.Equal(covered.OrderBy(t => t.Name), declared.OrderBy(t => t.Name));
    }
}
