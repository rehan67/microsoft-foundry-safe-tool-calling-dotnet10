using OrderAgent.Application.Contracts;
using OrderAgent.Domain.Entities;

namespace OrderAgent.Application.Abstractions;

/// <summary>
/// Read boundary enforcing tenant-scoped order queries.
/// </summary>
public interface IOrderReadRepository
{
    Task<OrderStatusResponse?> GetStatusForTenantAsync(
        int orderId,
        string callerTenantId,
        CancellationToken ct = default);

    Task<Order?> GetForTenantAsync(
        int orderId,
        string callerTenantId,
        CancellationToken ct = default);

    Task<OrderStatusResponse?> GetStatusUsingFilterOnlyAsync(
        int orderId,
        CancellationToken ct = default);
}
