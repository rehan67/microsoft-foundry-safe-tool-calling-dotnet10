using Microsoft.EntityFrameworkCore;
using OrderAgent.Application.Abstractions;
using OrderAgent.Application.Contracts;
using OrderAgent.Domain.Entities;

namespace OrderAgent.Infrastructure.Persistence;

/// <summary>
/// EF Core read repository pushing tenant isolation predicates directly into SQL.
/// </summary>
public sealed class OrderReadRepository(OrdersDbContext db) : IOrderReadRepository
{
    public Task<OrderStatusResponse?> GetStatusForTenantAsync(
        int orderId,
        string callerTenantId,
        CancellationToken ct = default) =>
        db.Orders
            .AsNoTracking()
            .Where(o => o.OrderId == orderId
                     && o.TenantId == callerTenantId)
            .Select(o => new OrderStatusResponse(o.OrderId, o.Status))
            .SingleOrDefaultAsync(ct);

    public Task<Order?> GetForTenantAsync(
        int orderId,
        string callerTenantId,
        CancellationToken ct = default) =>
        db.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.OrderId == orderId && o.TenantId == callerTenantId, ct);

    public Task<OrderStatusResponse?> GetStatusUsingFilterOnlyAsync(
        int orderId,
        CancellationToken ct = default) =>
        db.Orders
            .AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new OrderStatusResponse(o.OrderId, o.Status))
            .SingleOrDefaultAsync(ct);
}
