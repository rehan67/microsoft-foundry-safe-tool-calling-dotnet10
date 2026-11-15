namespace OrderAgent.Application.Contracts;

/// <summary>
/// Minimal order projection safe to return into an AI agent tool output.
/// </summary>
public sealed record OrderStatusResponse(int OrderId, string Status);
