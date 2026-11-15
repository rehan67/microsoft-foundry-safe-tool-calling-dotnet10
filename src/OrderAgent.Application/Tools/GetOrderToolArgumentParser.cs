using System.Text.Json;
using OrderAgent.Application.Contracts;

namespace OrderAgent.Application.Tools;

/// <summary>
/// Re-validates model-proposed tool arguments before downstream API or query execution.
/// </summary>
public static class GetOrderToolArgumentParser
{
    public static bool TryParseOrderId(string rawArgumentsJson, out int orderId)
    {
        orderId = 0;
        if (string.IsNullOrWhiteSpace(rawArgumentsJson))
            return false;

        GetOrderArguments? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<GetOrderArguments>(rawArgumentsJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (parsed is null || parsed.OrderId <= 0)
            return false;

        orderId = parsed.OrderId;
        return true;
    }
}
