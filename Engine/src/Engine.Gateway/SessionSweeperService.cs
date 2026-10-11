using Engine.Session;
using Microsoft.Extensions.Options;

namespace Engine.Gateway;

/// <summary>
/// Backstop for sessions the socket loop didn't clean up itself. The loop
/// evicts on close and on replacement, so this mostly catches edge cases.
/// </summary>
public sealed class SessionSweeperService(
    SessionManager sessions,
    IOptions<SessionLifecycleOptions> options,
    ILogger<SessionSweeperService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(settings.SweepInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                int evicted = sessions.EvictIdle(settings.IdleThreshold);
                if (evicted > 0)
                    logger.LogInformation("Evicted {Evicted} idle session(s); {Active} still active",
                        evicted, sessions.ActiveSessionCount);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }
}