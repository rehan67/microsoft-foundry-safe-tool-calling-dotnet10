using OrderAgent.Application.Abstractions;
using OrderAgent.Application.Contracts;
using OrderAgent.Domain.Entities;

namespace OrderAgent.Application.UseCases;

/// <summary>
/// Application use case coordinating tenant-scoped order status queries.
/// </summary>
public sealed class GetOrderStatusHandler(IOrderReadRepository repository)
{
    public Task<OrderStatusResponse?> HandleAsync(
        int orderId,
        string callerTenantId,
        CancellationToken ct = default) =>
        repository.GetStatusForTenantAsync(orderId, callerTenantId, ct);

    public Task<Order?> GetEntityForAuthorizationAsync(
        int orderId,
        string callerTenantId,
        CancellationToken ct = default) =>
        repository.GetForTenantAsync(orderId, callerTenantId, ct);

    public Task<OrderStatusResponse?> HandleWithGlobalFilterOnlyAsync(
        int orderId,
        CancellationToken ct = default) =>
        repository.GetStatusUsingFilterOnlyAsync(orderId, ct);
}
