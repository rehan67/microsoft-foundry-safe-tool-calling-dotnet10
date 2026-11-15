using Microsoft.EntityFrameworkCore;
using OrderAgent.Domain.Entities;

namespace OrderAgent.Infrastructure.Persistence;

/// <summary>
/// Seeds sample multi-tenant orders matching Episode 01 scenarios (1042 -> Tenant A, 2099 -> Tenant B).
/// </summary>
public static class OrderSeeder
{
    public static async Task SeedSampleOrdersAsync(OrdersDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        if (await db.Orders.IgnoreQueryFilters().AnyAsync(ct))
            return;

        db.Orders.AddRange(
            new Order { OrderId = 1042, TenantId = "tenant-a", CustomerUserId = "u_101", Status = "Shipped" },
            new Order { OrderId = 2099, TenantId = "tenant-b", CustomerUserId = "u_209", Status = "Processing" }
        );

        await db.SaveChangesAsync(ct);
    }
}
