using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Engine.Domain.Operations;
using Engine.Protocol;
using Engine.Session;

namespace Engine.Gateway;

public static class SessionSocketLoop
{
    private const string SessionExpiredReason =
        "session-expired: this session was evicted; rehydrate from your saved snapshot and operation log";

    private const string ServerBusyReason =
        "server-busy: the server is running its maximum number of concurrent operations; try again shortly";

    public static async Task RunAsync(
        WebSocket socket, SessionManager sessions, OperationRegistry registry,
        ExecutionPolicy execution, int maxMessageBytes, ILogger logger, CancellationToken ct)
    {
        // The session this connection currently owns, exactly one at a time:
        // adopting a new one evicts the previous, and ending the connection evicts the current.
        Guid? sessionId = null;

        void AdoptSession(Guid newId)
        {
            if (sessionId is Guid previous) sessions.Evict(previous); // no-op if Create already swapped it out
            sessionId = newId;
        }

        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var json = await BoundedMessageReader.ReadAsync(socket, maxMessageBytes, ct);
                if (json is null) break; // client closed

                ClientMessage? message;
                try { message = JsonSerializer.Deserialize<ClientMessage>(json, ProtocolJson.Options); }
                catch (JsonException ex)
                {
                    await SendAsync(socket, new ErrorMessage("bad-request", ex.Message), ct);
                    continue;
                }

                if (message is null)
                {
                    await SendAsync(socket, new ErrorMessage("bad-request", "The message was empty"), ct);
                    continue;
                }

                // One bad message must never end the connection. The handlers deal with the failures
                // we expect; this is the net for the ones we didn't.
                try
                {
                    switch (message)
                    {
                        case CreateSessionMessage create:
                            await HandleCreateSession(socket, sessions, create, sessionId, logger, ct, AdoptSession);
                            break;

                        case RehydrateSessionMessage rehydrate:
                            await HandleRehydrate(socket, sessions, registry, execution, rehydrate, logger, ct, AdoptSession);
                            break;

                        case ExecuteOperationMessage execute when sessionId is Guid id:
                            if (sessions.TryGet(id) is null)
                            {
                                // Evicted underneath us (idle sweep). Every Execute still gets exactly one terminal message.
                                sessionId = null;
                                await SendAsync(socket, new OperationFailedMessage(execute.RequestId, SessionExpiredReason), ct);
                                break;
                            }
                            await HandleExecute(socket, sessions, registry, execution, id, execute, logger, ct);
                            break;

                        case ExecuteOperationMessage:
                            await SendAsync(socket, new ErrorMessage("no-session", "Create a session first"), ct);
                            break;
                    }
                }
                catch (Exception ex) when (ex is not (OperationCanceledException or WebSocketException or MessageTooLargeException))
                {
                    logger.LogError(ex, "Unexpected error in {Context}", "HandleMessage");
                    await SendAsync(socket, new ErrorMessage("internal-error", "The server could not process that message"), ct);
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or IOException)
        {
            // Client vanished without a close handshake (closed tab, dropped network). Routine, not a crash.
        }
        catch (MessageTooLargeException ex)
        {
            try
            {
                await SendAsync(socket, new ErrorMessage("message-too-large", ex.Message), ct);
                await socket.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, "message too large", ct);
            }
            catch (WebSocketException)
            {
                // Peer already gone: nothing more to tell it.
            }
        }
        catch (OperationCanceledException)
        {
            // Request aborted or server shutting down.
        }
        finally
        {
            if (sessionId is Guid owned) sessions.Evict(owned);
        }
    }

    private static async Task HandleCreateSession(
        WebSocket socket, SessionManager sessions, CreateSessionMessage create,
        Guid? replacing, ILogger logger, CancellationToken ct, Action<Guid> adoptSession)
    {
        try
        {
            // Dataset construction runs first, so an invalid payload fails before touching the old session.
            var dataset = DatasetFactory.Create(create.DatasetType, create.InitialPayload);
            var session = sessions.Create(dataset, replacing);
            adoptSession(session.Id);
            await SendAsync(socket, new SessionReadyMessage(session.Id), ct);
        }
        catch (SessionCapacityException ex)
        {
            await SendAsync(socket, new ErrorMessage("server-busy", ex.Message), ct);
        }
        catch (Exception ex) when (IsClientInputException(ex))
        {
            await SendAsync(socket, new ErrorMessage("invalid-create-session", ex.Message), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in {Context}", "CreateSession");
            await SendAsync(socket, new ErrorMessage("internal-error", "Failed to create session"), ct);
        }
    }

    private static async Task HandleRehydrate(
        WebSocket socket, SessionManager sessions, OperationRegistry registry, ExecutionPolicy execution,
        RehydrateSessionMessage rehydrate, ILogger logger, CancellationToken ct, Action<Guid> adoptSession)
    {
        Guid? createdSessionId = null;

        void DiscardPartialSession()
        {
            if (createdSessionId is Guid orphaned) sessions.Evict(orphaned); // don't leak a half-built session
        }

        try
        {
            var initialDataset = DatasetFactory.Create(rehydrate.DatasetType, rehydrate.InitialPayload);

            // The whole replay shares ONE context, so a long history can't buy unlimited work
            // by being split into many operations. The slot is released before any socket write.
            using (var lease = await execution.TryEnterAsync(ct))
            {
                if (lease is null)
                {
                    await SendAsync(socket, new ErrorMessage("server-busy", ServerBusyReason), ct);
                    return;
                }

                var session = sessions.Create(initialDataset);
                createdSessionId = session.Id;
                var context = execution.CreateContext(ct);

                foreach (var logged in rehydrate.OpLog ?? [])
                {
                    if (logged is null) throw new ArgumentException("opLog entries must not be null");
                    var replayHandler = registry.TryGet(logged.OperationId)
                        ?? throw new InvalidOperationException($"Unknown operation '{logged.OperationId}' during rehydration");
                    replayHandler.Execute(sessions, session.Id, logged.Options ?? new Dictionary<string, JsonElement>(), context);
                }
            }

            var newId = createdSessionId!.Value;
            adoptSession(newId); // only now, so a failed rehydrate leaves the old session usable
            await SendAsync(socket, new SessionReadyMessage(newId), ct);
        }
        catch (SessionCapacityException ex)
        {
            await SendAsync(socket, new ErrorMessage("server-busy", ex.Message), ct);
        }
        catch (OperationCanceledException)
        {
            DiscardPartialSession();
            throw; // connection is going away: RunAsync handles it quietly
        }
        catch (OperationAbortedException ex) when (ex.Reason == OperationAbortReason.Cancelled)
        {
            DiscardPartialSession();
        }
        catch (OperationAbortedException ex)
        {
            DiscardPartialSession();
            await SendAsync(socket, new ErrorMessage("rehydrate-aborted", $"operation-aborted: {ex.Message}"), ct);
        }
        catch (Exception ex) when (IsClientInputException(ex))
        {
            DiscardPartialSession();
            await SendAsync(socket, new ErrorMessage("invalid-rehydrate", ex.Message), ct);
        }
        catch (Exception ex)
        {
            DiscardPartialSession();
            logger.LogError(ex, "Unexpected error in {Context}", "Rehydrate");
            await SendAsync(socket, new ErrorMessage("internal-error", "Failed to rehydrate session"), ct);
        }
    }

    private static async Task HandleExecute(
        WebSocket socket, SessionManager sessions, OperationRegistry registry, ExecutionPolicy execution,
        Guid sessionId, ExecuteOperationMessage execute, ILogger logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(execute.OperationId))
        {
            await SendAsync(socket, new OperationFailedMessage(execute.RequestId, "operationId is required"), ct);
            return;
        }

        var handler = registry.TryGet(execute.OperationId);
        if (handler is null)
        {
            await SendAsync(socket, new ErrorMessage("unknown-operation", execute.OperationId), ct);
            return;
        }

        var options = execute.Options ?? new Dictionary<string, JsonElement>();

        OperationOutcome outcome;
        try
        {
            // Held only while the operation computes, never while results are written to the
            // socket: a slow reader must not be able to hold execution capacity hostage.
            using var lease = await execution.TryEnterAsync(ct);
            if (lease is null)
            {
                await SendAsync(socket, new OperationFailedMessage(execute.RequestId, ServerBusyReason), ct);
                return;
            }

            outcome = handler.Execute(sessions, sessionId, options, execution.CreateContext(ct));
        }
        catch (OperationCanceledException)
        {
            throw; // connection is going away: RunAsync handles it quietly
        }
        catch (OperationAbortedException ex) when (ex.Reason == OperationAbortReason.Cancelled)
        {
            return; // cancelled mid-operation because the connection died: nobody is listening
        }
        catch (OperationAbortedException ex)
        {
            await SendAsync(socket, new OperationFailedMessage(execute.RequestId, $"operation-aborted: {ex.Message}"), ct);
            return;
        }
        catch (DatasetMismatchException ex)
        {
            await SendAsync(socket, new OperationFailedMessage(execute.RequestId, $"wrong-dataset: {ex.Message}"), ct);
            return;
        }
        catch (Exception ex) when (IsClientInputException(ex))
        {
            await SendAsync(socket, new OperationFailedMessage(execute.RequestId, $"Invalid options: {ex.Message}"), ct);
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in {Context}", "ExecuteOperation");
            await SendAsync(socket, new OperationFailedMessage(execute.RequestId, "Internal error executing operation"), ct);
            return;
        }

        foreach (var traceMessage in TraceStreamingPolicy.BuildMessages(outcome.Trace))
            await SendAsync(socket, traceMessage, ct);

        if (!outcome.Succeeded)
        {
            await SendAsync(socket, new OperationFailedMessage(execute.RequestId, outcome.FailureReason ?? "unknown"), ct);
            return;
        }

        // A coalesced trace leaves the client nothing to animate, so hand it the resulting dataset.
        DatasetSnapshotDto? finalState =
            TraceStreamingPolicy.WasCoalesced(outcome.Trace) && outcome.CommittedState is not null
                ? DatasetSnapshots.TryCreate(outcome.CommittedState)
                : null;

        await SendAsync(socket, new OperationCompleteMessage(
            execute.RequestId,
            outcome.Value is not null ? JsonSerializer.SerializeToElement(outcome.Value, ProtocolJson.Options) : null,
            finalState), ct);
    }

    private static bool IsClientInputException(Exception ex) =>
        ex is FormatException or KeyNotFoundException or InvalidOperationException or ArgumentException;

    private static Task SendAsync(WebSocket socket, ServerMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, ProtocolJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }
}