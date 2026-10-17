namespace Engine.Gateway;

public sealed class ExecutionLimitsOptions
{
    public int MaxConcurrentOperations { get; set; } = Math.Max(1, Environment.ProcessorCount);

    /// <summary>
    /// Most operations one client address may have running or queued at once, so a single machine cannot
    /// occupy every slot. Default: half the slots (at least one). Zero or less = no per-client limit.
    /// </summary>
    public int MaxConcurrentOperationsPerClient { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Wall-clock backstop. Non-deterministic by nature (depends on machine speed).</summary>
    public TimeSpan OperationTimeLimit { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Deterministic ceiling, same result on any machine. Starting value, not measured: tune it.</summary>
    public long MaxWorkUnits { get; set; } = 200_000_000;
}