namespace Engine.Domain.Tracing;

/// <summary>
/// An internal record of one atomic step during an operation's execution.
/// Description stays human-readable (for logs); Payload carries structured
/// data for anything that needs to consume the step programmatically
/// (animation, a real frontend) without parsing Description.
/// </summary>
public sealed record DomainStepEvent(string StepType, string Description, object? Payload = null);