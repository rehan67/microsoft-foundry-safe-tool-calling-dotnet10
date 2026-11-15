using System.Text.Json.Serialization;

namespace OrderAgent.Application.Contracts;

/// <summary>
/// Strict JSON payload matching the get_order tool schema (additionalProperties: false).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GetOrderArguments(
    [property: JsonPropertyName("orderId")] int OrderId);
