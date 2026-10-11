using System.Net.WebSockets;
using Engine.Gateway;
using Engine.Session;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SessionLifecycleOptions>(builder.Configuration.GetSection("SessionLifecycle"));
builder.Services.Configure<ConnectionLimitsOptions>(builder.Configuration.GetSection("ConnectionLimits"));
builder.Services.Configure<ExecutionLimitsOptions>(builder.Configuration.GetSection("ExecutionLimits"));

builder.Services.AddSingleton(sp =>
    new SessionManager(sp.GetRequiredService<IOptions<SessionLifecycleOptions>>().Value.MaxSessions));
builder.Services.AddSingleton(_ => OperationCatalog.BuildDefault());
builder.Services.AddSingleton<ExecutionPolicy>();
builder.Services.AddSingleton(sp =>
    new UploadBudget(sp.GetRequiredService<IOptions<ConnectionLimitsOptions>>().Value.MaxInFlightUploadBytes));
builder.Services.AddHostedService<SessionSweeperService>();

var app = builder.Build();
app.UseWebSockets();

app.MapGet("/operations", (OperationRegistry registry) =>
    registry.All
        .Select(h => new
        {
            id = h.OperationId,
            datasetType = h.DatasetType,
            exampleOptions = OperationExamples.TryGetElement(h.OperationId)
        })
        .OrderBy(o => o.datasetType).ThenBy(o => o.id));

app.Map("/session", async (
    HttpContext context,
    SessionManager sessions,
    OperationRegistry registry,
    ExecutionPolicy execution,
    UploadBudget uploadBudget,
    IOptions<ConnectionLimitsOptions> limits) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    try
    {
        await SessionSocketLoop.RunAsync(
            socket, sessions, registry, execution, limits.Value, uploadBudget, app.Logger, context.RequestAborted);
    }
    catch (Exception ex) when (ex is not (OperationCanceledException or WebSocketException or IOException))
    {
        app.Logger.LogError(ex, "Session loop terminated unexpectedly");
    }
});

app.Run();

public partial class Program; // exposes Program for WebApplicationFactory<Program> in tests