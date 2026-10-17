using System.Net.WebSockets;
using Engine.Gateway;
using Engine.Session;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SessionLifecycleOptions>(builder.Configuration.GetSection("SessionLifecycle"));
builder.Services.Configure<ConnectionLimitsOptions>(builder.Configuration.GetSection("ConnectionLimits"));
builder.Services.Configure<ExecutionLimitsOptions>(builder.Configuration.GetSection("ExecutionLimits"));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));

// Browsers may read /operations and /health from the listed origins only (any origin while the list is empty).
// The WebSocket endpoint is not covered by CORS, so /session checks the Origin header itself.
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IOptions<SecurityOptions>>((cors, security) =>
    cors.AddDefaultPolicy(policy =>
    {
        policy.WithMethods("GET");
        if (security.Value.AllowedOrigins.Length == 0) policy.AllowAnyOrigin();
        else policy.WithOrigins(security.Value.AllowedOrigins);
    }));

builder.Services.AddSingleton(sp =>
{
    var lifecycle = sp.GetRequiredService<IOptions<SessionLifecycleOptions>>().Value;
    return new SessionManager(lifecycle.MaxSessions, lifecycle.MaxTotalDatasetBytes);
});
builder.Services.AddSingleton(_ => OperationCatalog.BuildDefault());
builder.Services.AddSingleton<ExecutionPolicy>();
builder.Services.AddSingleton(sp =>
    new UploadBudget(sp.GetRequiredService<IOptions<ConnectionLimitsOptions>>().Value.MaxInFlightUploadBytes));
builder.Services.AddSingleton(sp =>
    new ConnectionLimiter(sp.GetRequiredService<IOptions<ConnectionLimitsOptions>>().Value.MaxConnectionsPerIp));
builder.Services.AddHostedService<SessionSweeperService>();

var app = builder.Build();
var wsLimits = app.Services.GetRequiredService<IOptions<ConnectionLimitsOptions>>().Value;
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = wsLimits.KeepAliveInterval,
    KeepAliveTimeout = wsLimits.KeepAliveTimeout
});
app.UseCors();
app.Use(async (context, next) =>
{
    // Responses here are JSON; stop browsers from guessing another type and treating them as something else.
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next();
});

if (app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value.AllowedOrigins.Length == 0)
    app.Logger.LogWarning(
        "Security:AllowedOrigins is empty, so any website may open a session and call the API. Set it before exposing the server.");

app.MapGet("/health", (SessionManager sessions) =>
    new
    {
        status = "ok",
        sessions = sessions.ActiveSessionCount,
        maxSessions = sessions.MaxSessions,
        datasetBytes = sessions.TotalDatasetBytes,
        maxDatasetBytes = sessions.MaxTotalDatasetBytes
    });

app.MapGet("/operations", (OperationRegistry registry) =>
    registry.All
        .Select(h =>
        {
            var descriptor = OperationDescriptors.TryGet(h.OperationId);
            return new
            {
                id = h.OperationId,
                datasetType = h.DatasetType,
                label = descriptor?.Label ?? h.OperationId,
                category = descriptor?.Category ?? "Other",
                description = descriptor?.Description ?? "",
                options = descriptor?.Options ?? [],
                exampleOptions = OperationExamples.TryGetElement(h.OperationId)
            };
        })
        .OrderBy(o => o.datasetType).ThenBy(o => o.id));

app.Map("/session", async (
    HttpContext context,
    SessionManager sessions,
    OperationRegistry registry,
    ExecutionPolicy execution,
    UploadBudget uploadBudget,
    ConnectionLimiter connections,
    IOptions<SecurityOptions> security,
    IOptions<ConnectionLimitsOptions> limits) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var origin = context.Request.Headers.Origin.ToString();
    if (!OriginPolicy.IsAllowed(origin, security.Value.AllowedOrigins))
    {
        app.Logger.LogWarning("Refused WebSocket from origin {Origin}", origin);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    var clientKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    using var slot = connections.TryAcquire(clientKey);
    if (slot is null)
    {
        app.Logger.LogWarning("Refused WebSocket from {Address}: too many connections", context.Connection.RemoteIpAddress);
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    try
    {
        await SessionSocketLoop.RunAsync(
            socket, sessions, registry, execution, limits.Value, uploadBudget, app.Logger, clientKey, context.RequestAborted);
    }
    catch (Exception ex) when (ex is not (OperationCanceledException or WebSocketException or IOException))
    {
        app.Logger.LogError(ex, "Session loop terminated unexpectedly");
    }
});

app.Run();

public partial class Program; // exposes Program for WebApplicationFactory<Program> in tests