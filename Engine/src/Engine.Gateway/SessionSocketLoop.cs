using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Engine.Domain.Operations;
using Engine.Domain.Tracing;
using Engine.Protocol;
using Engine.Session;

namespace Engine.Gateway;

public static class SessionSocketLoop
{
    private const string SessionExpiredReason =
        "session-expired: this session was evicted; rehydrate from your saved snapshot and operation log";

    private const string DuplicateRequestReason =
        "duplicate-request: this requestId was already used on this connection, so it was not run again; use getState to see the current dataset";

    private const string ServerBusyReason =
        "server-busy: the server is running its maximum number of concurrent operations; try again shortly";

    public static async Task RunAsync(
        WebSocket socket, SessionManager sessions, OperationRegistry registry,
        ExecutionPolicy execution, ConnectionLimitsOptions limits, UploadBudget uploadBudget,
        ILogger logger, CancellationToken ct)
    {
        // The session this connection currently owns, exactly one at a time:
        // adopting a new one evicts the previous, and ending the connection evicts the current.
        Guid? sessionId = null;
        using var uploads = new UploadAssembler(limits, uploadBudget);
        var recentRequests = new RecentRequestIds();

        void AdoptSession(Guid newId)
        {
            if (sessionId is Guid previous) sessions.Evict(previous); // no-op if Create already swapped it out
            sessionId = newId;
        }

        // One bad message must never end the connection. The handlers deal with the failures
        // we expect; this is the net for the ones we didn't.
        async Task DispatchAsync(ClientMessage message)
        {
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

                    case UploadChunkMessage chunk:
                        await HandleUploadChunk(chunk);
                        break;

                    case GetStateMessage getState when sessionId is Guid stateSessionId:
                        await HandleGetState(socket, sessions, stateSessionId, getState, ct);
                        break;

                    case GetStateMessage:
                        await SendAsync(socket, new ErrorMessage("no-session", "Create a session first"), ct);
                        break;

                    case ExecuteOperationMessage execute when sessionId is Guid id:
                        if (sessions.TryGet(id) is null)
                        {
                            // Evicted underneath us (idle sweep). Every Execute still gets exactly one terminal message.
                            sessionId = null;
                            await SendAsync(socket, new OperationFailedMessage(execute.RequestId, SessionExpiredReason), ct);
                            break;
                        }
                        if (!recentRequests.TryRecord(execute.RequestId))
                        {
                            await SendAsync(socket, new OperationFailedMessage(execute.RequestId, DuplicateRequestReason), ct);
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

        async Task HandleUploadChunk(UploadChunkMessage chunk)
        {
            var result = uploads.Accept(chunk);
            switch (result.Status)
            {
                case UploadStatus.InProgress:
                    return;

                case UploadStatus.Rejected:
                    await SendAsync(socket, new ErrorMessage(result.Code!, result.Message!), ct);
                    return;
            }

            try
            {
                ClientMessage? inner;
                try { inner = JsonSerializer.Deserialize<ClientMessage>(result.Json!, ProtocolJson.Options); }
                catch (JsonException ex)
                {
                    await SendAsync(socket, new ErrorMessage("bad-request", ex.Message), ct);
                    return;
                }

                if (inner is CreateSessionMessage or RehydrateSessionMessage)
                    await DispatchAsync(inner);
                else
                    await SendAsync(socket, new ErrorMessage("invalid-upload",
                        "An upload can only carry a createSession or rehydrateSession message"), ct);
            }
            finally
            {
                uploads.Reset(); // the buffered text is no longer needed, whatever happened
            }
        }

        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                // A client that starts an upload and goes quiet must not hold buffered memory forever:
                // while one is in progress, give each read only the time the upload has left.
                var timeLeft = uploads.TimeRemaining;
                using var uploadClock = timeLeft is null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct);
                if (timeLeft is { } left) uploadClock!.CancelAfter(left);

                var json = await BoundedMessageReader.ReadAsync(socket, limits.MaxMessageBytes, uploadClock?.Token ?? ct);
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

                await DispatchAsync(message);
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
            if (!ct.IsCancellationRequested)
                logger.LogWarning("Closing a connection whose upload did not finish within {Timeout}", limits.UploadTimeout);
            // Otherwise: request aborted or server shutting down.
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
        OperationContext context;
        TimeSpan elapsed;
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

            context = execution.CreateContext(ct);
            var started = Stopwatch.GetTimestamp();
            try
            {
                outcome = handler.Execute(sessions, sessionId, options, context);
            }
            finally
            {
                elapsed = Stopwatch.GetElapsedTime(started);
            }
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

        // The resulting dataset rides along when it is small, so the client rarely needs getState. A
        // coalesced trace leaves nothing to animate, so then it is sent whenever it fits the larger cap.
        DatasetSnapshotDto? finalState = outcome.CommittedState is null ? null
            : TraceStreamingPolicy.WasCoalesced(outcome.Trace)
                ? DatasetSnapshots.TryCreate(outcome.CommittedState)
                : DatasetSnapshots.TryCreateInline(outcome.CommittedState);

        await SendAsync(socket, new OperationCompleteMessage(
            execute.RequestId,
            outcome.Value is not null ? JsonSerializer.SerializeToElement(outcome.Value, ProtocolJson.Options) : null,
            finalState,
            new ExecutionStatsDto(
                TraceSummary.Of(outcome.Trace).TotalCount,
                context.WorkUnits,
                Math.Round(elapsed.TotalMilliseconds, 3))), ct);
    }

    private static async Task HandleGetState(
        WebSocket socket, SessionManager sessions, Guid sessionId, GetStateMessage request, CancellationToken ct)
    {
        var dataset = sessions.TryGetDataset(sessionId);
        if (dataset is null)
        {
            await SendAsync(socket, new OperationFailedMessage(request.RequestId, SessionExpiredReason), ct);
            return;
        }

        var snapshot = DatasetSnapshots.TryCreate(dataset);
        await SendAsync(socket, snapshot is null
            ? new OperationFailedMessage(request.RequestId, "state-too-large: this dataset is too big to send as a snapshot")
            : new StateMessage(request.RequestId, snapshot), ct);
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